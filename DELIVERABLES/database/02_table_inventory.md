# Table Inventory

Generated from the live database, 25 August 2026. **36 tables, 429 columns.**

`nvarchar` lengths are in bytes as SQL Server reports them; divide by two for characters.

---

## __EFMigrationsHistory

| Column | Type | Nullability |
| --- | --- | --- |
| `MigrationId` | nvarchar(300) | NOT NULL |
| `ProductVersion` | nvarchar(64) | NOT NULL |

## Address

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `Label` | nvarchar(400) | NULL |
| `FullName` | nvarchar(400) | NOT NULL |
| `Phone` | nvarchar(64) | NULL |
| `Line1` | nvarchar(1024) | NOT NULL |
| `Line2` | nvarchar(1024) | NULL |
| `City` | nvarchar(400) | NOT NULL |
| `State` | nvarchar(400) | NULL |
| `PostalCode` | nvarchar(64) | NULL |
| `Country` | nvarchar(4) | NOT NULL |
| `IsDefault` | bit | NOT NULL |
| `DeletedAt` | datetime2 | NULL |

## Appointment

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `PatientId` | nvarchar(60) | NOT NULL |
| `StartsAt` | datetime2 | NOT NULL |
| `Minutes` | int | NOT NULL |
| `Kind` | nvarchar(80) | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `Notes` | nvarchar(max) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `StaffUserId` | nvarchar(60) | NULL |
| `ReminderSentAt` | datetime2 | NULL |
| `CancelledAt` | datetime2 | NULL |
| `CancelledReason` | nvarchar(1024) | NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## AspNetRoleClaims

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | int | NOT NULL |
| `RoleId` | nvarchar(60) | NOT NULL |
| `ClaimType` | nvarchar(1024) | NULL |
| `ClaimValue` | nvarchar(1024) | NULL |

## AspNetRoles

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Name` | nvarchar(512) | NULL |
| `NormalizedName` | nvarchar(512) | NULL |
| `ConcurrencyStamp` | nvarchar(1024) | NULL |

## AspNetUserClaims

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | int | NOT NULL |
| `UserId` | nvarchar(60) | NOT NULL |
| `ClaimType` | nvarchar(1024) | NULL |
| `ClaimValue` | nvarchar(1024) | NULL |

## AspNetUserLogins

| Column | Type | Nullability |
| --- | --- | --- |
| `LoginProvider` | nvarchar(256) | NOT NULL |
| `ProviderKey` | nvarchar(256) | NOT NULL |
| `ProviderDisplayName` | nvarchar(1024) | NULL |
| `UserId` | nvarchar(60) | NOT NULL |

## AspNetUserRoles

| Column | Type | Nullability |
| --- | --- | --- |
| `UserId` | nvarchar(60) | NOT NULL |
| `RoleId` | nvarchar(60) | NOT NULL |

## AspNetUsers

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `Role` | nvarchar(80) | NOT NULL |
| `IsActive` | bit | NOT NULL |
| `LastLoginAt` | datetime2 | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |
| `UserName` | nvarchar(512) | NULL |
| `NormalizedUserName` | nvarchar(512) | NULL |
| `Email` | nvarchar(512) | NULL |
| `NormalizedEmail` | nvarchar(512) | NULL |
| `EmailConfirmed` | bit | NOT NULL |
| `PasswordHash` | nvarchar(1024) | NULL |
| `SecurityStamp` | nvarchar(1024) | NULL |
| `ConcurrencyStamp` | nvarchar(1024) | NULL |
| `PhoneNumber` | nvarchar(1024) | NULL |
| `PhoneNumberConfirmed` | bit | NOT NULL |
| `TwoFactorEnabled` | bit | NOT NULL |
| `LockoutEnd` | datetimeoffset | NULL |
| `LockoutEnabled` | bit | NOT NULL |
| `AccessFailedCount` | int | NOT NULL |

## AspNetUserTokens

| Column | Type | Nullability |
| --- | --- | --- |
| `UserId` | nvarchar(60) | NOT NULL |
| `LoginProvider` | nvarchar(256) | NOT NULL |
| `Name` | nvarchar(256) | NOT NULL |
| `Value` | nvarchar(1024) | NULL |

## AuditLog

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `Action` | nvarchar(80) | NOT NULL |
| `Entity` | nvarchar(80) | NOT NULL |
| `EntityId` | nvarchar(60) | NULL |
| `Detail` | nvarchar(max) | NULL |
| `Ip` | nvarchar(128) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `ActorEmail` | nvarchar(512) | NULL |
| `UserAgent` | nvarchar(1024) | NULL |

## Brand

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `Slug` | nvarchar(256) | NOT NULL |
| `LogoUrl` | nvarchar(2048) | NULL |
| `About` | nvarchar(max) | NULL |
| `IsActive` | bit | NOT NULL |

## Cart

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Token` | nvarchar(128) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `Currency` | nvarchar(6) | NOT NULL |
| `PromoCode` | nvarchar(256) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## CartItem

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `CartId` | nvarchar(60) | NOT NULL |
| `VariantId` | nvarchar(60) | NOT NULL |
| `Qty` | int | NOT NULL |
| `LensOptionCodes` | nvarchar(4096) | NULL |
| `PrescriptionDraft` | nvarchar(max) | NULL |
| `PrescriptionId` | nvarchar(60) | NULL |
| `TryOnSnapshotId` | nvarchar(60) | NULL |
| `UnitPriceMinor` | int | NOT NULL |
| `LensPriceMinor` | int | NOT NULL |
| `CreatedAt` | datetime2 | NOT NULL |

## Category

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `Slug` | nvarchar(256) | NOT NULL |
| `ParentId` | nvarchar(60) | NULL |
| `Position` | int | NOT NULL |

## DataSubjectRequest

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `PatientId` | nvarchar(60) | NULL |
| `Email` | nvarchar(512) | NOT NULL |
| `Kind` | nvarchar(80) | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `CustomerMessage` | nvarchar(max) | NULL |
| `StaffNotes` | nvarchar(max) | NULL |
| `HandledByUserId` | nvarchar(60) | NULL |
| `HandledAt` | datetime2 | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## Frame

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Sku` | nvarchar(256) | NOT NULL |
| `Slug` | nvarchar(256) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `BrandId` | nvarchar(60) | NULL |
| `Description` | nvarchar(max) | NULL |
| `Shape` | nvarchar(80) | NULL |
| `Material` | nvarchar(80) | NULL |
| `RimType` | nvarchar(80) | NOT NULL |
| `Gender` | nvarchar(80) | NOT NULL |
| `FaceShapes` | nvarchar(4096) | NULL |
| `LensWidthMm` | float | NULL |
| `BridgeWidthMm` | float | NULL |
| `TempleLengthMm` | float | NULL |
| `LensHeightMm` | float | NULL |
| `TotalWidthMm` | float | NULL |
| `WeightGrams` | float | NULL |
| `SizeBand` | nvarchar(80) | NULL |
| `BasePriceMinor` | int | NOT NULL |
| `CompareAtMinor` | int | NULL |
| `CostMinor` | int | NULL |
| `AllowFrameOnly` | bit | NOT NULL |
| `RequiresPrescription` | bit | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `IsFeatured` | bit | NOT NULL |
| `Position` | int | NOT NULL |
| `MetaTitle` | nvarchar(1024) | NULL |
| `MetaDesc` | nvarchar(1024) | NULL |
| `SearchText` | nvarchar(2048) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## FrameCategory

| Column | Type | Nullability |
| --- | --- | --- |
| `FrameId` | nvarchar(60) | NOT NULL |
| `CategoryId` | nvarchar(60) | NOT NULL |

## FrameVariant

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `FrameId` | nvarchar(60) | NOT NULL |
| `Sku` | nvarchar(256) | NOT NULL |
| `ColorName` | nvarchar(400) | NOT NULL |
| `ColorHex` | nvarchar(32) | NULL |
| `Barcode` | nvarchar(256) | NULL |
| `PriceMinor` | int | NULL |
| `StockQty` | int | NOT NULL |
| `LowStockAt` | int | NOT NULL |
| `IsActive` | bit | NOT NULL |
| `Position` | int | NOT NULL |
| `TryOnImageUrl` | nvarchar(2048) | NULL |
| `AnchorLeftX` | float | NOT NULL |
| `AnchorLeftY` | float | NOT NULL |
| `AnchorRightX` | float | NOT NULL |
| `AnchorRightY` | float | NOT NULL |
| `TryOnScaleAdj` | float | NOT NULL |
| `TryOnOpacity` | float | NOT NULL |

## ImportJob

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Kind` | nvarchar(80) | NOT NULL |
| `Filename` | nvarchar(400) | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `TotalRows` | int | NOT NULL |
| `OkRows` | int | NOT NULL |
| `ErrorRows` | int | NOT NULL |
| `Report` | nvarchar(max) | NULL |
| `CreatedBy` | nvarchar(60) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `FinishedAt` | datetime2 | NULL |
| `IsDryRun` | bit | NOT NULL |

## LensOption

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Group` | nvarchar(80) | NOT NULL |
| `Code` | nvarchar(256) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `Description` | nvarchar(1024) | NULL |
| `PriceMinor` | int | NOT NULL |
| `MinSphere` | float | NULL |
| `MaxSphere` | float | NULL |
| `MaxCylinder` | float | NULL |
| `Requires` | nvarchar(4096) | NULL |
| `Excludes` | nvarchar(4096) | NULL |
| `IsDefault` | bit | NOT NULL |
| `IsActive` | bit | NOT NULL |
| `Position` | int | NOT NULL |

## MediaAsset

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Url` | nvarchar(2048) | NOT NULL |
| `ThumbUrl` | nvarchar(2048) | NULL |
| `Filename` | nvarchar(400) | NOT NULL |
| `MimeType` | nvarchar(256) | NULL |
| `SizeBytes` | int | NULL |
| `Width` | int | NULL |
| `Height` | int | NULL |
| `Tags` | nvarchar(4096) | NULL |
| `UploadedBy` | nvarchar(60) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `StorageKey` | nvarchar(2048) | NULL |
| `ThumbStorageKey` | nvarchar(2048) | NULL |
| `StorageProvider` | nvarchar(80) | NOT NULL |
| `DeletedAt` | datetime2 | NULL |
| `PurgedAt` | datetime2 | NULL |
| `PurgeError` | nvarchar(1024) | NULL |
| `PurgeAttempts` | int | NOT NULL |

## Order

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `OrderNo` | nvarchar(64) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `PatientId` | nvarchar(60) | NULL |
| `Email` | nvarchar(512) | NOT NULL |
| `Phone` | nvarchar(64) | NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `PaymentStatus` | nvarchar(80) | NOT NULL |
| `FulfilmentStatus` | nvarchar(80) | NOT NULL |
| `Currency` | nvarchar(6) | NOT NULL |
| `SubtotalMinor` | int | NOT NULL |
| `LensTotalMinor` | int | NOT NULL |
| `DiscountMinor` | int | NOT NULL |
| `ShippingMinor` | int | NOT NULL |
| `TaxMinor` | int | NOT NULL |
| `TotalMinor` | int | NOT NULL |
| `PromoCode` | nvarchar(256) | NULL |
| `PromotionId` | nvarchar(60) | NULL |
| `ShippingAddressId` | nvarchar(60) | NULL |
| `BillingAddressId` | nvarchar(60) | NULL |
| `Notes` | nvarchar(max) | NULL |
| `InternalNotes` | nvarchar(max) | NULL |
| `PlacedAt` | datetime2 | NOT NULL |
| `PaidAt` | datetime2 | NULL |
| `ShippedAt` | datetime2 | NULL |
| `DeliveredAt` | datetime2 | NULL |
| `CancelledAt` | datetime2 | NULL |

## OrderItem

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `OrderId` | nvarchar(60) | NOT NULL |
| `VariantId` | nvarchar(60) | NULL |
| `TitleSnapshot` | nvarchar(400) | NOT NULL |
| `SkuSnapshot` | nvarchar(256) | NOT NULL |
| `ImageSnapshot` | nvarchar(2048) | NULL |
| `Qty` | int | NOT NULL |
| `UnitPriceMinor` | int | NOT NULL |
| `LensPriceMinor` | int | NOT NULL |
| `TotalMinor` | int | NOT NULL |
| `LensOptionCodes` | nvarchar(4096) | NULL |
| `LensSummary` | nvarchar(1024) | NULL |
| `PrescriptionId` | nvarchar(60) | NULL |
| `PrescriptionSnapshot` | nvarchar(max) | NULL |
| `TryOnSnapshotUrl` | nvarchar(2048) | NULL |
| `LabStatus` | nvarchar(80) | NOT NULL |
| `LabRef` | nvarchar(256) | NULL |

## OutboxEmail

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `ToAddress` | nvarchar(512) | NOT NULL |
| `ToName` | nvarchar(400) | NULL |
| `Subject` | nvarchar(1024) | NOT NULL |
| `HtmlBody` | nvarchar(max) | NOT NULL |
| `TextBody` | nvarchar(max) | NULL |
| `Template` | nvarchar(80) | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `Attempts` | int | NOT NULL |
| `LastError` | nvarchar(1024) | NULL |
| `NextAttemptAt` | datetime2 | NULL |
| `SentAt` | datetime2 | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `RelatedEntity` | nvarchar(80) | NULL |
| `RelatedEntityId` | nvarchar(60) | NULL |

## Patient

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `FileNo` | nvarchar(40) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `FirstName` | nvarchar(400) | NOT NULL |
| `LastName` | nvarchar(400) | NOT NULL |
| `Email` | nvarchar(512) | NULL |
| `Phone` | nvarchar(64) | NULL |
| `DateOfBirth` | datetime2 | NULL |
| `Gender` | nvarchar(80) | NULL |
| `Notes` | nvarchar(max) | NULL |
| `PdMm` | float | NULL |
| `PdNearMm` | float | NULL |
| `FaceMetrics` | nvarchar(max) | NULL |
| `Tags` | nvarchar(4096) | NULL |
| `ConsentMarketing` | bit | NOT NULL |
| `ConsentDataAt` | datetime2 | NULL |
| `ConsentVersion` | nvarchar(80) | NULL |
| `RetentionUntil` | datetime2 | NULL |
| `DeletedAt` | datetime2 | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## PatientDocument

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `PatientId` | nvarchar(60) | NOT NULL |
| `Kind` | nvarchar(80) | NOT NULL |
| `Label` | nvarchar(400) | NULL |
| `Url` | nvarchar(2048) | NOT NULL |
| `MimeType` | nvarchar(256) | NULL |
| `SizeBytes` | int | NULL |
| `CreatedAt` | datetime2 | NOT NULL |

## Payment

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `OrderId` | nvarchar(60) | NOT NULL |
| `Provider` | nvarchar(80) | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `AmountMinor` | int | NOT NULL |
| `Currency` | nvarchar(6) | NOT NULL |
| `ProviderRef` | nvarchar(256) | NULL |
| `RawPayload` | nvarchar(max) | NULL |
| `Error` | nvarchar(1024) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |
| `IdempotencyKey` | nvarchar(256) | NULL |
| `RefundedMinor` | int | NOT NULL |

## Prescription

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `PatientId` | nvarchar(60) | NOT NULL |
| `Source` | nvarchar(80) | NOT NULL |
| `IssuedAt` | datetime2 | NOT NULL |
| `ExpiresAt` | datetime2 | NULL |
| `Prescriber` | nvarchar(400) | NULL |
| `Clinic` | nvarchar(400) | NULL |
| `DocumentUrl` | nvarchar(2048) | NULL |
| `VerifiedBy` | nvarchar(60) | NULL |
| `VerifiedAt` | datetime2 | NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `OdSphere` | float | NULL |
| `OdCylinder` | float | NULL |
| `OdAxis` | int | NULL |
| `OdAdd` | float | NULL |
| `OdPrism` | float | NULL |
| `OdPrismBase` | nvarchar(16) | NULL |
| `OdPdMm` | float | NULL |
| `OsSphere` | float | NULL |
| `OsCylinder` | float | NULL |
| `OsAxis` | int | NULL |
| `OsAdd` | float | NULL |
| `OsPrism` | float | NULL |
| `OsPrismBase` | nvarchar(16) | NULL |
| `OsPdMm` | float | NULL |
| `OdSegHeightMm` | float | NULL |
| `OsSegHeightMm` | float | NULL |
| `Notes` | nvarchar(max) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## ProductImage

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `VariantId` | nvarchar(60) | NOT NULL |
| `Url` | nvarchar(2048) | NOT NULL |
| `ThumbUrl` | nvarchar(2048) | NULL |
| `Alt` | nvarchar(1024) | NULL |
| `Role` | nvarchar(80) | NOT NULL |
| `Width` | int | NULL |
| `Height` | int | NULL |
| `Position` | int | NOT NULL |
| `CreatedAt` | datetime2 | NOT NULL |

## Promotion

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `Description` | nvarchar(max) | NULL |
| `Code` | nvarchar(256) | NULL |
| `Kind` | nvarchar(80) | NOT NULL |
| `Value` | int | NOT NULL |
| `MaxDiscountMinor` | int | NULL |
| `MinSubtotalMinor` | int | NOT NULL |
| `MinQty` | int | NOT NULL |
| `BrandIds` | nvarchar(4096) | NULL |
| `CategoryIds` | nvarchar(4096) | NULL |
| `FrameIds` | nvarchar(4096) | NULL |
| `FirstOrderOnly` | bit | NOT NULL |
| `StartsAt` | datetime2 | NULL |
| `EndsAt` | datetime2 | NULL |
| `UsageLimit` | int | NULL |
| `UsageLimitPerUser` | int | NULL |
| `UsageCount` | int | NOT NULL |
| `Stackable` | bit | NOT NULL |
| `Priority` | int | NOT NULL |
| `IsActive` | bit | NOT NULL |
| `BannerText` | nvarchar(1024) | NULL |
| `BannerColor` | nvarchar(32) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## Setting

| Column | Type | Nullability |
| --- | --- | --- |
| `Key` | nvarchar(256) | NOT NULL |
| `Value` | nvarchar(max) | NOT NULL |
| `Group` | nvarchar(80) | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## Shipment

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `OrderId` | nvarchar(60) | NOT NULL |
| `Carrier` | nvarchar(80) | NOT NULL |
| `Service` | nvarchar(400) | NULL |
| `TrackingNumber` | nvarchar(256) | NULL |
| `TrackingUrl` | nvarchar(2048) | NULL |
| `LabelUrl` | nvarchar(2048) | NULL |
| `CostMinor` | int | NOT NULL |
| `Status` | nvarchar(80) | NOT NULL |
| `ShippedAt` | datetime2 | NULL |
| `DeliveredAt` | datetime2 | NULL |
| `ProviderRef` | nvarchar(256) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |

## ShippingRate

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `Name` | nvarchar(400) | NOT NULL |
| `Country` | nvarchar(4) | NOT NULL |
| `Region` | nvarchar(400) | NULL |
| `MinSubtotalMinor` | int | NOT NULL |
| `MaxSubtotalMinor` | int | NULL |
| `PriceMinor` | int | NOT NULL |
| `EtaDaysMin` | int | NOT NULL |
| `EtaDaysMax` | int | NOT NULL |
| `Carrier` | nvarchar(80) | NULL |
| `IsActive` | bit | NOT NULL |
| `Position` | int | NOT NULL |
| `EffectiveFrom` | datetime2 | NULL |
| `EffectiveTo` | datetime2 | NULL |
| `Code` | nvarchar(256) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |
| `UpdatedAt` | datetime2 | NOT NULL |

## TryOnSession

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `UserId` | nvarchar(60) | NULL |
| `PatientId` | nvarchar(60) | NULL |
| `Source` | nvarchar(80) | NOT NULL |
| `PhotoUrl` | nvarchar(2048) | NULL |
| `FaceData` | nvarchar(max) | NULL |
| `CreatedAt` | datetime2 | NOT NULL |

## TryOnSnapshot

| Column | Type | Nullability |
| --- | --- | --- |
| `Id` | nvarchar(60) | NOT NULL |
| `SessionId` | nvarchar(60) | NOT NULL |
| `VariantId` | nvarchar(60) | NOT NULL |
| `ImageUrl` | nvarchar(2048) | NOT NULL |
| `CreatedAt` | datetime2 | NOT NULL |

