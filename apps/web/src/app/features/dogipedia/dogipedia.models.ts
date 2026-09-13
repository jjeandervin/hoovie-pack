export interface BreedImage {
  thumbUrl: string | null; mediumUrl: string | null; largeUrl?: string | null;
  author: string | null; license: string | null; licenseUrl: string | null;
  source: string | null; sourceUrl: string | null;
}
export interface BreedGalleryImage extends BreedImage { id: string; }
export interface RelatedBreed { id: string; name: string; groupName: string | null; image: BreedImage | null; }
export interface BreedCard {
  id: string; name: string; descriptionExcerpt: string | null; groupName: string | null;
  lifeMinYears: number | null; lifeMaxYears: number | null; image: BreedImage | null;
}
export interface BreedPage {
  items: BreedCard[]; page: number; pageSize: number; totalItems: number;
  totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean;
}
export interface BreedTraits {
  energy: number | null; trainability: number | null; barking: number | null;
  grooming: number | null; shedding: number | null; drooling: number | null;
  goodWithChildren: number | null; goodWithDogs: number | null; goodWithStrangers: number | null;
  apartmentFriendly: number | null; exerciseMinutes: number | null; temperament: string[];
}
export interface BreedDetail {
  id: string; name: string; description: string | null; hypoallergenic: boolean | null;
  group: { id: string; name: string } | null;
  origin: { country: string | null; region: string | null; era: string | null };
  life: { minYears: number | null; maxYears: number | null };
  maleWeight: { minKg: number | null; maxKg: number | null };
  femaleWeight: { minKg: number | null; maxKg: number | null };
  maleHeight: { minCm: number | null; maxCm: number | null };
  femaleHeight: { minCm: number | null; maxCm: number | null };
  coat: { type: string | null; length: string | null; colors: string[] };
  traits: BreedTraits; otherNames: string[]; recognizedBy: string[];
  sources: { title: string | null; url: string | null }[];
  primaryImage: BreedGalleryImage | null; images: BreedGalleryImage[]; relatedBreeds: RelatedBreed[];
}
