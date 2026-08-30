import assert from 'node:assert/strict';
import test from 'node:test';
import { uploadToPresignedUrl } from '../src/app/core/presigned-upload.ts';

test('uploads the original file with only the required headers', async () => {
  const file = new File(['photo bytes'], 'poster.jpg', { type: 'image/jpeg' });
  const requiredHeaders = {
    'Content-Type': 'image/jpeg',
    'x-amz-meta-upload': 'one-file'
  };
  let request;

  await uploadToPresignedUrl(
    file,
    { uploadUrl: 'https://bucket.s3.example/files/one/original?signature=test', requiredHeaders },
    async (input, init) => {
      request = { input, init };
      return new Response(null, { status: 200 });
    }
  );

  assert.equal(request.input, 'https://bucket.s3.example/files/one/original?signature=test');
  assert.equal(request.init.method, 'PUT');
  assert.equal(request.init.body, file);
  assert.equal(request.init.credentials, 'omit');
  assert.deepEqual(
    [...request.init.headers.entries()],
    [['content-type', 'image/jpeg'], ['x-amz-meta-upload', 'one-file']]
  );
  assert.equal(request.init.headers.has('authorization'), false);
});

test('rejects an authorization header without making a request', async () => {
  const file = new File(['photo bytes'], 'poster.jpg', { type: 'image/jpeg' });
  let called = false;

  await assert.rejects(
    uploadToPresignedUrl(
      file,
      { uploadUrl: 'https://bucket.s3.example/upload', requiredHeaders: { Authorization: 'Bearer secret' } },
      async () => {
        called = true;
        return new Response(null, { status: 200 });
      }
    ),
    { name: 'PresignedUploadError', message: 'The file could not be uploaded. Please try again.' }
  );
  assert.equal(called, false);
});

test('does not expose a failed S3 response body', async () => {
  const file = new File(['photo bytes'], 'poster.jpg', { type: 'image/jpeg' });
  const secretResponse = '<Error><Message>signature detail</Message><RequestId>secret-request</RequestId></Error>';

  await assert.rejects(
    uploadToPresignedUrl(
      file,
      { uploadUrl: 'https://bucket.s3.example/upload', requiredHeaders: { 'Content-Type': 'image/jpeg' } },
      async () => new Response(secretResponse, { status: 403 })
    ),
    (error) => {
      assert.equal(error.name, 'PresignedUploadError');
      assert.equal(error.status, 403);
      assert.equal(error.message, 'The file could not be uploaded. Please try again.');
      assert.equal(error.message.includes('signature detail'), false);
      assert.equal(error.message.includes('secret-request'), false);
      return true;
    }
  );
});

test('sanitizes network failures', async () => {
  const file = new File(['photo bytes'], 'poster.jpg', { type: 'image/jpeg' });

  await assert.rejects(
    uploadToPresignedUrl(
      file,
      { uploadUrl: 'https://bucket.s3.example/upload', requiredHeaders: { 'Content-Type': 'image/jpeg' } },
      async () => {
        throw new Error('internal network detail');
      }
    ),
    { name: 'PresignedUploadError', message: 'The file could not be uploaded. Please try again.' }
  );
});
