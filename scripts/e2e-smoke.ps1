[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-EnvValue([string]$Name) {
    $line = Get-Content -LiteralPath (Join-Path $repoRoot '.env') |
        Where-Object { $_ -like "$Name=*" } |
        Select-Object -First 1
    if (-not $line) {
        throw "Missing $Name in .env. Copy .env.example to .env first."
    }

    return $line.Substring($Name.Length + 1)
}

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-OidcToken([string]$Username, [string]$Password) {
    $verifierBytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($verifierBytes)
    $verifier = ConvertTo-Base64Url $verifierBytes
    $sha = [Security.Cryptography.SHA256]::Create()
    $challenge = ConvertTo-Base64Url ($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($verifier)))
    $redirect = 'http://localhost:4200/auth/callback'
    $query = 'client_id=hooviepack-web&redirect_uri=' + [uri]::EscapeDataString($redirect) +
        '&response_type=code&scope=' + [uri]::EscapeDataString('openid profile email offline_access') +
        '&code_challenge=' + [uri]::EscapeDataString($challenge) +
        '&code_challenge_method=S256'
    $browserId = [Guid]::NewGuid().ToString('N')
    $loginPage = Join-Path ([IO.Path]::GetTempPath()) "hooviepack-login-$browserId.html"
    $cookieJar = Join-Path ([IO.Path]::GetTempPath()) "hooviepack-login-$browserId.cookies"
    try {
        $null = & curl.exe -sS `
            -c $cookieJar `
            -o $loginPage `
            "http://localhost:8081/realms/hooviepack/protocol/openid-connect/auth?$query"
        if ($LASTEXITCODE -ne 0) {
            throw 'Keycloak authorization request failed.'
        }

        $loginContent = Get-Content -LiteralPath $loginPage -Raw
        $match = [regex]::Match(
            $loginContent,
            '<form[^>]*id="kc-form-login"[^>]*action="([^"]+)"',
            [Text.RegularExpressions.RegexOptions]::Singleline)
        if (-not $match.Success) {
            throw 'Keycloak login form was not found.'
        }

        $action = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
        $finalUri = & curl.exe -sS `
            -b $cookieJar `
            -c $cookieJar `
            -L `
            -o NUL `
            -w '%{url_effective}' `
            --data-urlencode "username=$Username" `
            --data-urlencode "password=$Password" `
            --data-urlencode 'credentialId=' `
            $action
        if ($LASTEXITCODE -ne 0) {
            throw 'Keycloak credential submission failed.'
        }
    } finally {
        Remove-Item -LiteralPath $loginPage, $cookieJar -Force -ErrorAction SilentlyContinue
    }

    $codeMatch = [regex]::Match($finalUri, '[?&]code=([^&]+)')
    if (-not $codeMatch.Success) {
        throw "OIDC callback did not contain an authorization code. Final URI: $finalUri"
    }

    $token = Invoke-RestMethod `
        -Method Post `
        -Uri 'http://localhost:8081/realms/hooviepack/protocol/openid-connect/token' `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body @{
            grant_type = 'authorization_code'
            client_id = 'hooviepack-web'
            redirect_uri = $redirect
            code = [uri]::UnescapeDataString($codeMatch.Groups[1].Value)
            code_verifier = $verifier
        }
    return $token.access_token
}

function New-KeycloakUser(
    [hashtable]$Headers,
    [string]$Username,
    [string]$Password,
    [string]$FirstName) {
    $payload = @{
        username = $Username
        email = "$Username@example.test"
        firstName = $FirstName
        lastName = 'Pack'
        enabled = $true
        emailVerified = $true
        credentials = @(@{
            type = 'password'
            value = $Password
            temporary = $false
        })
    } | ConvertTo-Json -Depth 6
    $response = Invoke-WebRequest `
        -Method Post `
        -Uri 'http://localhost:8081/admin/realms/hooviepack/users' `
        -Headers $Headers `
        -ContentType 'application/json' `
        -Body $payload `
        -UseBasicParsing
    if ($response.StatusCode -ne 201) {
        throw "Keycloak user creation returned $($response.StatusCode)."
    }
}

function Send-MediaUpload(
    [hashtable]$Headers,
    [string]$FilePath,
    [string]$ContentType,
    [ValidateSet('avatar', 'dogPhoto', 'postPhoto')]
    [string]$Purpose,
    [string]$FamilyId = '') {
    $file = Get-Item -LiteralPath $FilePath
    $payload = [ordered]@{
        fileName = $file.Name
        contentType = $ContentType
        size = $file.Length
        purpose = $Purpose
    }
    if ($FamilyId) {
        $payload.familyId = $FamilyId
    }

    $upload = Invoke-RestMethod `
        -Method Post `
        -Uri 'http://localhost:5000/api/media/uploads' `
        -Headers $Headers `
        -ContentType 'application/json' `
        -Body ($payload | ConvertTo-Json -Compress)
    if (-not $upload.fileId -or -not $upload.uploadUrl -or -not $upload.uploadToken) {
        throw 'Upload initialization returned an incomplete response.'
    }

    $curlArguments = @('-sS', '-o', 'NUL', '-w', '%{http_code}', '-X', 'PUT')
    foreach ($header in $upload.requiredHeaders.PSObject.Properties) {
        if ($header.Name -ieq 'Authorization') {
            throw 'Upload initialization unexpectedly returned an Authorization header.'
        }
        $curlArguments += @('-H', "$($header.Name): $($header.Value)")
    }
    $curlArguments += @('--upload-file', $FilePath, $upload.uploadUrl)
    $uploadCode = & curl.exe @curlArguments
    if ($LASTEXITCODE -ne 0 -or [int]$uploadCode -lt 200 -or [int]$uploadCode -ge 300) {
        throw "Direct file upload failed with HTTP $uploadCode."
    }

    return [ordered]@{
        fileId = $upload.fileId
        uploadToken = $upload.uploadToken
    }
}

function Get-JsonRequestStatus(
    [string]$Method,
    [string]$Uri,
    [hashtable]$Headers,
    [object]$Body) {
    try {
        $response = Invoke-WebRequest `
            -Method $Method `
            -Uri $Uri `
            -Headers $Headers `
            -ContentType 'application/json' `
            -Body ($Body | ConvertTo-Json -Depth 8 -Compress) `
            -UseBasicParsing
        return [int]$response.StatusCode
    } catch {
        if ($null -eq $_.Exception.Response) {
            throw
        }
        return [int]$_.Exception.Response.StatusCode
    }
}

$adminToken = Invoke-RestMethod `
    -Method Post `
    -Uri 'http://localhost:8081/realms/master/protocol/openid-connect/token' `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body @{
        grant_type = 'password'
        client_id = 'admin-cli'
        username = (Get-EnvValue 'KEYCLOAK_ADMIN')
        password = (Get-EnvValue 'KEYCLOAK_ADMIN_PASSWORD')
    }
$adminHeaders = @{ Authorization = "Bearer $($adminToken.access_token)" }
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$ownerName = "e2e.owner.$suffix"
$memberName = "e2e.member.$suffix"
$outsiderName = "e2e.outsider.$suffix"
$password = "Hp-E2e-$suffix-Aa1!"

New-KeycloakUser $adminHeaders $ownerName $password 'E2E Owner'
New-KeycloakUser $adminHeaders $memberName $password 'E2E Member'
New-KeycloakUser $adminHeaders $outsiderName $password 'E2E Outsider'

$ownerToken = New-OidcToken $ownerName $password
$memberToken = New-OidcToken $memberName $password
$outsiderToken = New-OidcToken $outsiderName $password
$ownerHeaders = @{ Authorization = "Bearer $ownerToken" }
$memberHeaders = @{ Authorization = "Bearer $memberToken" }
$outsiderHeaders = @{ Authorization = "Bearer $outsiderToken" }

$ownerMe = Invoke-RestMethod -Uri 'http://localhost:5000/api/me' -Headers $ownerHeaders
$null = Invoke-RestMethod -Uri 'http://localhost:5000/api/me' -Headers $memberHeaders
$null = Invoke-RestMethod -Uri 'http://localhost:5000/api/me' -Headers $outsiderHeaders
$family = Invoke-RestMethod `
    -Method Post `
    -Uri 'http://localhost:5000/api/families' `
    -Headers $ownerHeaders `
    -ContentType 'application/json' `
    -Body (@{ name = 'E2E Star Pack'; description = 'Disposable integration test family' } | ConvertTo-Json)
$invite = Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:5000/api/families/$($family.id)/invites" `
    -Headers $ownerHeaders `
    -ContentType 'application/json' `
    -Body (@{ expiresInDays = 1 } | ConvertTo-Json)
$joined = Invoke-RestMethod `
    -Method Post `
    -Uri 'http://localhost:5000/api/families/join' `
    -Headers $memberHeaders `
    -ContentType 'application/json' `
    -Body (@{ inviteCode = $invite.inviteCode } | ConvertTo-Json)
$members = Invoke-RestMethod `
    -Uri "http://localhost:5000/api/families/$($family.id)/members" `
    -Headers $ownerHeaders

$tempPng = Join-Path ([IO.Path]::GetTempPath()) "hooviepack-e2e-$suffix.png"
$tempDownload = Join-Path ([IO.Path]::GetTempPath()) "hooviepack-e2e-download-$suffix.png"
try {
    $testImage = 'iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAzSURBVFhH7c4hAQAwCABB8tGDiMs3PAHAnHjz6uJV/stiju0AAAAAAAAAAAAAAAAAAAAazNWMiHfl7J0AAAAASUVORK5CYII='
    [IO.File]::WriteAllBytes($tempPng, [Convert]::FromBase64String($testImage))

    $avatarFile = Send-MediaUpload $ownerHeaders $tempPng 'image/png' 'avatar'
    $ownerProfile = Invoke-RestMethod `
        -Method Post `
        -Uri 'http://localhost:5000/api/me/avatar' `
        -Headers $ownerHeaders `
        -ContentType 'application/json' `
        -Body ($avatarFile | ConvertTo-Json -Compress)

    $postPhotoFile = Send-MediaUpload $ownerHeaders $tempPng 'image/png' 'postPhoto' $family.id
    $post = Invoke-RestMethod `
        -Method Post `
        -Uri "http://localhost:5000/api/families/$($family.id)/posts" `
        -Headers $ownerHeaders `
        -ContentType 'application/json' `
        -Body (@{
            content = 'Hermes found the sunny spot.'
            photoFiles = @($postPhotoFile)
        } | ConvertTo-Json -Depth 4)
    $comment = Invoke-RestMethod `
        -Method Post `
        -Uri "http://localhost:5000/api/posts/$($post.id)/comments" `
        -Headers $memberHeaders `
        -ContentType 'application/json' `
        -Body (@{ content = 'A perfect pack update.' } | ConvertTo-Json)
    $reaction = Invoke-RestMethod `
        -Method Post `
        -Uri "http://localhost:5000/api/posts/$($post.id)/reactions/paw" `
        -Headers $memberHeaders `
        -ContentType 'application/json' `
        -Body '{}'
    $dogPhotoFile = Send-MediaUpload $ownerHeaders $tempPng 'image/png' 'dogPhoto' $family.id
    $dog = Invoke-RestMethod `
        -Method Post `
        -Uri "http://localhost:5000/api/families/$($family.id)/dogs" `
        -Headers $ownerHeaders `
        -ContentType 'application/json' `
        -Body (@{
            name = 'Hermes Hoovie Star'
            breed = 'Pembroke Welsh Corgi'
            favoriteThing = 'Sunny naps'
            photoFile = $dogPhotoFile
            removePhoto = $false
        } | ConvertTo-Json -Depth 4)
    $memberFeed = Invoke-RestMethod `
        -Uri "http://localhost:5000/api/families/$($family.id)/posts?page=1&pageSize=10" `
        -Headers $memberHeaders
    $photoUrl = $post.photos[0].url
    $downloadTicket = Invoke-RestMethod `
        -Uri "http://localhost:5000$photoUrl" `
        -Headers $ownerHeaders
    $ownerMediaCode = & curl.exe -sS -o $tempDownload -w '%{http_code}' `
        $downloadTicket.downloadUrl
    if ($LASTEXITCODE -ne 0) {
        throw 'Direct file download failed.'
    }
    $outsiderMediaCode = & curl.exe -sS -o NUL -w '%{http_code}' `
        -H "Authorization: Bearer $outsiderToken" `
        "http://localhost:5000$photoUrl"
    $invalidTypeCode = Get-JsonRequestStatus `
        'Post' `
        'http://localhost:5000/api/media/uploads' `
        $ownerHeaders `
        @{
            fileName = 'not-an-image.txt'
            contentType = 'text/plain'
            size = 12
            purpose = 'postPhoto'
            familyId = $family.id
        }
    $invalidSizeCode = Get-JsonRequestStatus `
        'Post' `
        'http://localhost:5000/api/media/uploads' `
        $ownerHeaders `
        @{
            fileName = 'empty.png'
            contentType = 'image/png'
            size = 0
            purpose = 'postPhoto'
            familyId = $family.id
        }
    try {
        $null = Invoke-WebRequest `
            -Uri "http://localhost:5000/api/families/$($family.id)" `
            -Headers $outsiderHeaders `
            -UseBasicParsing
        $outsiderFamilyCode = 200
    } catch {
        $outsiderFamilyCode = [int]$_.Exception.Response.StatusCode
    }

    $result = [ordered]@{
        oidcUsers = 3
        ownerProfileSynced = [bool]$ownerMe.id
        avatarAssociated = [bool]$ownerProfile.avatarUrl
        familyRole = $family.role
        joinedRole = $joined.role
        memberCount = @($members).Count
        photoPostCount = @($memberFeed.items).Count
        commentCreated = [bool]$comment.id
        pawReactionCount = $reaction.reactions.counts.paw
        dogName = $dog.name
        dogCanManage = $dog.canManage
        ownerMediaStatus = [int]$ownerMediaCode
        ownerDownloadTicketIssued = [bool]$downloadTicket.downloadUrl
        outsiderMediaStatus = [int]$outsiderMediaCode
        outsiderFamilyStatus = $outsiderFamilyCode
        invalidContentTypeStatus = $invalidTypeCode
        invalidSizeStatus = $invalidSizeCode
        downloadedBytes = (Get-Item -LiteralPath $tempDownload).Length
    }
    if ($result.familyRole -ne 'owner' -or
        $result.joinedRole -ne 'member' -or
        $result.memberCount -ne 2 -or
        $result.photoPostCount -ne 1 -or
        $result.pawReactionCount -ne 1 -or
        -not $result.avatarAssociated -or
        $result.ownerMediaStatus -ne 200 -or
        -not $result.ownerDownloadTicketIssued -or
        $result.outsiderMediaStatus -ne 404 -or
        $result.outsiderFamilyStatus -ne 404 -or
        $result.invalidContentTypeStatus -ne 400 -or
        $result.invalidSizeStatus -ne 400 -or
        $result.downloadedBytes -le 0) {
        throw "Unexpected E2E result: $($result | ConvertTo-Json -Compress)"
    }

    $result | ConvertTo-Json -Compress
} finally {
    Remove-Item -LiteralPath $tempPng, $tempDownload -Force -ErrorAction SilentlyContinue
}
