IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(30) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(512) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Role] nvarchar(40) NOT NULL,
        [IsActive] bit NOT NULL,
        [LastLoginAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(512) NULL,
        [SecurityStamp] nvarchar(512) NULL,
        [ConcurrencyStamp] nvarchar(512) NULL,
        [PhoneNumber] nvarchar(512) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Brand] (
        [Id] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(128) NOT NULL,
        [LogoUrl] nvarchar(1024) NULL,
        [About] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Brand] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Category] (
        [Id] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(128) NOT NULL,
        [ParentId] nvarchar(30) NULL,
        [Position] int NOT NULL,
        CONSTRAINT [PK_Category] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Category_Category_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Category] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [ImportJob] (
        [Id] nvarchar(30) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Filename] nvarchar(200) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [TotalRows] int NOT NULL,
        [OkRows] int NOT NULL,
        [ErrorRows] int NOT NULL,
        [Report] nvarchar(max) NULL,
        [CreatedBy] nvarchar(30) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [FinishedAt] datetime2 NULL,
        [IsDryRun] bit NOT NULL,
        CONSTRAINT [PK_ImportJob] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [LensOption] (
        [Id] nvarchar(30) NOT NULL,
        [Group] nvarchar(40) NOT NULL,
        [Code] nvarchar(128) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(512) NULL,
        [PriceMinor] int NOT NULL,
        [MinSphere] float NULL,
        [MaxSphere] float NULL,
        [MaxCylinder] float NULL,
        [Requires] nvarchar(2048) NULL,
        [Excludes] nvarchar(2048) NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Position] int NOT NULL,
        CONSTRAINT [PK_LensOption] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [MediaAsset] (
        [Id] nvarchar(30) NOT NULL,
        [Url] nvarchar(1024) NOT NULL,
        [ThumbUrl] nvarchar(1024) NULL,
        [Filename] nvarchar(200) NOT NULL,
        [MimeType] nvarchar(128) NULL,
        [SizeBytes] int NULL,
        [Width] int NULL,
        [Height] int NULL,
        [Tags] nvarchar(2048) NULL,
        [UploadedBy] nvarchar(30) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StorageKey] nvarchar(1024) NULL,
        [ThumbStorageKey] nvarchar(1024) NULL,
        [StorageProvider] nvarchar(40) NOT NULL,
        [DeletedAt] datetime2 NULL,
        [PurgedAt] datetime2 NULL,
        [PurgeError] nvarchar(512) NULL,
        [PurgeAttempts] int NOT NULL,
        CONSTRAINT [PK_MediaAsset] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [OutboxEmail] (
        [Id] nvarchar(30) NOT NULL,
        [ToAddress] nvarchar(256) NOT NULL,
        [ToName] nvarchar(200) NULL,
        [Subject] nvarchar(512) NOT NULL,
        [HtmlBody] nvarchar(max) NOT NULL,
        [TextBody] nvarchar(max) NULL,
        [Template] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(512) NULL,
        [NextAttemptAt] datetime2 NULL,
        [SentAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [RelatedEntity] nvarchar(40) NULL,
        [RelatedEntityId] nvarchar(30) NULL,
        CONSTRAINT [PK_OutboxEmail] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Promotion] (
        [Id] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Code] nvarchar(128) NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Value] int NOT NULL,
        [MaxDiscountMinor] int NULL,
        [MinSubtotalMinor] int NOT NULL,
        [MinQty] int NOT NULL,
        [BrandIds] nvarchar(2048) NULL,
        [CategoryIds] nvarchar(2048) NULL,
        [FrameIds] nvarchar(2048) NULL,
        [FirstOrderOnly] bit NOT NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        [UsageLimit] int NULL,
        [UsageLimitPerUser] int NULL,
        [UsageCount] int NOT NULL,
        [Stackable] bit NOT NULL,
        [Priority] int NOT NULL,
        [IsActive] bit NOT NULL,
        [BannerText] nvarchar(512) NULL,
        [BannerColor] nvarchar(16) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Promotion] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Setting] (
        [Key] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        [Group] nvarchar(40) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Setting] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [ShippingRate] (
        [Id] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Country] nvarchar(2) NOT NULL,
        [Region] nvarchar(200) NULL,
        [MinSubtotalMinor] int NOT NULL,
        [MaxSubtotalMinor] int NULL,
        [PriceMinor] int NOT NULL,
        [EtaDaysMin] int NOT NULL,
        [EtaDaysMax] int NOT NULL,
        [Carrier] nvarchar(40) NULL,
        [IsActive] bit NOT NULL,
        [Position] int NOT NULL,
        [EffectiveFrom] datetime2 NULL,
        [EffectiveTo] datetime2 NULL,
        [Code] nvarchar(128) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ShippingRate] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(30) NOT NULL,
        [ClaimType] nvarchar(512) NULL,
        [ClaimValue] nvarchar(512) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Address] (
        [Id] nvarchar(30) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [Label] nvarchar(200) NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Phone] nvarchar(32) NULL,
        [Line1] nvarchar(512) NOT NULL,
        [Line2] nvarchar(512) NULL,
        [City] nvarchar(200) NOT NULL,
        [State] nvarchar(200) NULL,
        [PostalCode] nvarchar(32) NULL,
        [Country] nvarchar(2) NOT NULL,
        [IsDefault] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_Address] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Address_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(30) NOT NULL,
        [ClaimType] nvarchar(512) NULL,
        [ClaimValue] nvarchar(512) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(128) NOT NULL,
        [ProviderKey] nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(512) NULL,
        [UserId] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(30) NOT NULL,
        [RoleId] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(30) NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Value] nvarchar(512) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [AuditLog] (
        [Id] nvarchar(30) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [Action] nvarchar(40) NOT NULL,
        [Entity] nvarchar(40) NOT NULL,
        [EntityId] nvarchar(30) NULL,
        [Detail] nvarchar(max) NULL,
        [Ip] nvarchar(64) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ActorEmail] nvarchar(256) NULL,
        [UserAgent] nvarchar(512) NULL,
        CONSTRAINT [PK_AuditLog] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditLog_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Cart] (
        [Id] nvarchar(30) NOT NULL,
        [Token] nvarchar(64) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [Currency] nvarchar(3) NOT NULL,
        [PromoCode] nvarchar(128) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Cart] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Cart_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Patient] (
        [Id] nvarchar(30) NOT NULL,
        [FileNo] nvarchar(20) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [FirstName] nvarchar(200) NOT NULL,
        [LastName] nvarchar(200) NOT NULL,
        [Email] nvarchar(256) NULL,
        [Phone] nvarchar(32) NULL,
        [DateOfBirth] datetime2 NULL,
        [Gender] nvarchar(40) NULL,
        [Notes] nvarchar(max) NULL,
        [PdMm] float NULL,
        [PdNearMm] float NULL,
        [FaceMetrics] nvarchar(max) NULL,
        [Tags] nvarchar(2048) NULL,
        [ConsentMarketing] bit NOT NULL,
        [ConsentDataAt] datetime2 NULL,
        [ConsentVersion] nvarchar(40) NULL,
        [RetentionUntil] datetime2 NULL,
        [DeletedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Patient] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Patient_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Frame] (
        [Id] nvarchar(30) NOT NULL,
        [Sku] nvarchar(128) NOT NULL,
        [Slug] nvarchar(128) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [BrandId] nvarchar(30) NULL,
        [Description] nvarchar(max) NULL,
        [Shape] nvarchar(40) NULL,
        [Material] nvarchar(40) NULL,
        [RimType] nvarchar(40) NOT NULL,
        [Gender] nvarchar(40) NOT NULL,
        [FaceShapes] nvarchar(2048) NULL,
        [LensWidthMm] float NULL,
        [BridgeWidthMm] float NULL,
        [TempleLengthMm] float NULL,
        [LensHeightMm] float NULL,
        [TotalWidthMm] float NULL,
        [WeightGrams] float NULL,
        [SizeBand] nvarchar(40) NULL,
        [BasePriceMinor] int NOT NULL,
        [CompareAtMinor] int NULL,
        [CostMinor] int NULL,
        [AllowFrameOnly] bit NOT NULL,
        [RequiresPrescription] bit NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [IsFeatured] bit NOT NULL,
        [Position] int NOT NULL,
        [MetaTitle] nvarchar(512) NULL,
        [MetaDesc] nvarchar(512) NULL,
        [SearchText] nvarchar(1024) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Frame] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Frame_Brand_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brand] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Appointment] (
        [Id] nvarchar(30) NOT NULL,
        [PatientId] nvarchar(30) NOT NULL,
        [StartsAt] datetime2 NOT NULL,
        [Minutes] int NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StaffUserId] nvarchar(30) NULL,
        [ReminderSentAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        [CancelledReason] nvarchar(512) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Appointment] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Appointment_AspNetUsers_StaffUserId] FOREIGN KEY ([StaffUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Appointment_Patient_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patient] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [DataSubjectRequest] (
        [Id] nvarchar(30) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [PatientId] nvarchar(30) NULL,
        [Email] nvarchar(256) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [CustomerMessage] nvarchar(max) NULL,
        [StaffNotes] nvarchar(max) NULL,
        [HandledByUserId] nvarchar(30) NULL,
        [HandledAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DataSubjectRequest] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DataSubjectRequest_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_DataSubjectRequest_Patient_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patient] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Order] (
        [Id] nvarchar(30) NOT NULL,
        [OrderNo] nvarchar(32) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [PatientId] nvarchar(30) NULL,
        [Email] nvarchar(256) NOT NULL,
        [Phone] nvarchar(32) NULL,
        [Status] nvarchar(40) NOT NULL,
        [PaymentStatus] nvarchar(40) NOT NULL,
        [FulfilmentStatus] nvarchar(40) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [SubtotalMinor] int NOT NULL,
        [LensTotalMinor] int NOT NULL,
        [DiscountMinor] int NOT NULL,
        [ShippingMinor] int NOT NULL,
        [TaxMinor] int NOT NULL,
        [TotalMinor] int NOT NULL,
        [PromoCode] nvarchar(128) NULL,
        [PromotionId] nvarchar(30) NULL,
        [ShippingAddressId] nvarchar(30) NULL,
        [BillingAddressId] nvarchar(30) NULL,
        [Notes] nvarchar(max) NULL,
        [InternalNotes] nvarchar(max) NULL,
        [PlacedAt] datetime2 NOT NULL,
        [PaidAt] datetime2 NULL,
        [ShippedAt] datetime2 NULL,
        [DeliveredAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        CONSTRAINT [PK_Order] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Order_Address_BillingAddressId] FOREIGN KEY ([BillingAddressId]) REFERENCES [Address] ([Id]),
        CONSTRAINT [FK_Order_Address_ShippingAddressId] FOREIGN KEY ([ShippingAddressId]) REFERENCES [Address] ([Id]),
        CONSTRAINT [FK_Order_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Order_Patient_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patient] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Order_Promotion_PromotionId] FOREIGN KEY ([PromotionId]) REFERENCES [Promotion] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [PatientDocument] (
        [Id] nvarchar(30) NOT NULL,
        [PatientId] nvarchar(30) NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Label] nvarchar(200) NULL,
        [Url] nvarchar(1024) NOT NULL,
        [MimeType] nvarchar(128) NULL,
        [SizeBytes] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PatientDocument] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PatientDocument_Patient_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patient] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Prescription] (
        [Id] nvarchar(30) NOT NULL,
        [PatientId] nvarchar(30) NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [IssuedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [Prescriber] nvarchar(200) NULL,
        [Clinic] nvarchar(200) NULL,
        [DocumentUrl] nvarchar(1024) NULL,
        [VerifiedBy] nvarchar(30) NULL,
        [VerifiedAt] datetime2 NULL,
        [Status] nvarchar(40) NOT NULL,
        [OdSphere] float NULL,
        [OdCylinder] float NULL,
        [OdAxis] int NULL,
        [OdAdd] float NULL,
        [OdPrism] float NULL,
        [OdPrismBase] nvarchar(8) NULL,
        [OdPdMm] float NULL,
        [OsSphere] float NULL,
        [OsCylinder] float NULL,
        [OsAxis] int NULL,
        [OsAdd] float NULL,
        [OsPrism] float NULL,
        [OsPrismBase] nvarchar(8) NULL,
        [OsPdMm] float NULL,
        [OdSegHeightMm] float NULL,
        [OsSegHeightMm] float NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Prescription] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Prescription_Patient_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patient] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [TryOnSession] (
        [Id] nvarchar(30) NOT NULL,
        [UserId] nvarchar(30) NULL,
        [PatientId] nvarchar(30) NULL,
        [Source] nvarchar(40) NOT NULL,
        [PhotoUrl] nvarchar(1024) NULL,
        [FaceData] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TryOnSession] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TryOnSession_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TryOnSession_Patient_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patient] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [FrameCategory] (
        [FrameId] nvarchar(30) NOT NULL,
        [CategoryId] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_FrameCategory] PRIMARY KEY ([FrameId], [CategoryId]),
        CONSTRAINT [FK_FrameCategory_Category_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Category] ([Id]),
        CONSTRAINT [FK_FrameCategory_Frame_FrameId] FOREIGN KEY ([FrameId]) REFERENCES [Frame] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [FrameVariant] (
        [Id] nvarchar(30) NOT NULL,
        [FrameId] nvarchar(30) NOT NULL,
        [Sku] nvarchar(128) NOT NULL,
        [ColorName] nvarchar(200) NOT NULL,
        [ColorHex] nvarchar(16) NULL,
        [Barcode] nvarchar(128) NULL,
        [PriceMinor] int NULL,
        [StockQty] int NOT NULL,
        [LowStockAt] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Position] int NOT NULL,
        [TryOnImageUrl] nvarchar(1024) NULL,
        [AnchorLeftX] float NOT NULL,
        [AnchorLeftY] float NOT NULL,
        [AnchorRightX] float NOT NULL,
        [AnchorRightY] float NOT NULL,
        [TryOnScaleAdj] float NOT NULL,
        [TryOnOpacity] float NOT NULL,
        CONSTRAINT [PK_FrameVariant] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FrameVariant_Frame_FrameId] FOREIGN KEY ([FrameId]) REFERENCES [Frame] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Payment] (
        [Id] nvarchar(30) NOT NULL,
        [OrderId] nvarchar(30) NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [AmountMinor] int NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [ProviderRef] nvarchar(128) NULL,
        [RawPayload] nvarchar(max) NULL,
        [Error] nvarchar(512) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [IdempotencyKey] nvarchar(128) NULL,
        [RefundedMinor] int NOT NULL,
        CONSTRAINT [PK_Payment] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payment_Order_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Order] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [Shipment] (
        [Id] nvarchar(30) NOT NULL,
        [OrderId] nvarchar(30) NOT NULL,
        [Carrier] nvarchar(40) NOT NULL,
        [Service] nvarchar(200) NULL,
        [TrackingNumber] nvarchar(128) NULL,
        [TrackingUrl] nvarchar(1024) NULL,
        [LabelUrl] nvarchar(1024) NULL,
        [CostMinor] int NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [ShippedAt] datetime2 NULL,
        [DeliveredAt] datetime2 NULL,
        [ProviderRef] nvarchar(128) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Shipment] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Shipment_Order_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Order] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [CartItem] (
        [Id] nvarchar(30) NOT NULL,
        [CartId] nvarchar(30) NOT NULL,
        [VariantId] nvarchar(30) NOT NULL,
        [Qty] int NOT NULL,
        [LensOptionCodes] nvarchar(2048) NULL,
        [PrescriptionDraft] nvarchar(max) NULL,
        [PrescriptionId] nvarchar(30) NULL,
        [TryOnSnapshotId] nvarchar(30) NULL,
        [UnitPriceMinor] int NOT NULL,
        [LensPriceMinor] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CartItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CartItem_Cart_CartId] FOREIGN KEY ([CartId]) REFERENCES [Cart] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CartItem_FrameVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [FrameVariant] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [OrderItem] (
        [Id] nvarchar(30) NOT NULL,
        [OrderId] nvarchar(30) NOT NULL,
        [VariantId] nvarchar(30) NULL,
        [TitleSnapshot] nvarchar(200) NOT NULL,
        [SkuSnapshot] nvarchar(128) NOT NULL,
        [ImageSnapshot] nvarchar(1024) NULL,
        [Qty] int NOT NULL,
        [UnitPriceMinor] int NOT NULL,
        [LensPriceMinor] int NOT NULL,
        [TotalMinor] int NOT NULL,
        [LensOptionCodes] nvarchar(2048) NULL,
        [LensSummary] nvarchar(512) NULL,
        [PrescriptionId] nvarchar(30) NULL,
        [PrescriptionSnapshot] nvarchar(max) NULL,
        [TryOnSnapshotUrl] nvarchar(1024) NULL,
        [LabStatus] nvarchar(40) NOT NULL,
        [LabRef] nvarchar(128) NULL,
        CONSTRAINT [PK_OrderItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItem_FrameVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [FrameVariant] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderItem_Order_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Order] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrderItem_Prescription_PrescriptionId] FOREIGN KEY ([PrescriptionId]) REFERENCES [Prescription] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [ProductImage] (
        [Id] nvarchar(30) NOT NULL,
        [VariantId] nvarchar(30) NOT NULL,
        [Url] nvarchar(1024) NOT NULL,
        [ThumbUrl] nvarchar(1024) NULL,
        [Alt] nvarchar(512) NULL,
        [Role] nvarchar(40) NOT NULL,
        [Width] int NULL,
        [Height] int NULL,
        [Position] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductImage] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductImage_FrameVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [FrameVariant] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE TABLE [TryOnSnapshot] (
        [Id] nvarchar(30) NOT NULL,
        [SessionId] nvarchar(30) NOT NULL,
        [VariantId] nvarchar(30) NOT NULL,
        [ImageUrl] nvarchar(1024) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TryOnSnapshot] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TryOnSnapshot_FrameVariant_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [FrameVariant] ([Id]),
        CONSTRAINT [FK_TryOnSnapshot_TryOnSession_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [TryOnSession] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Address_UserId] ON [Address] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Address_UserId_IsDefault] ON [Address] ([UserId], [IsDefault]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Appointment_PatientId_StartsAt] ON [Appointment] ([PatientId], [StartsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Appointment_StaffUserId_StartsAt] ON [Appointment] ([StaffUserId], [StartsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Appointment_StartsAt] ON [Appointment] ([StartsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Appointment_Status] ON [Appointment] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_IsActive] ON [AspNetUsers] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_Role] ON [AspNetUsers] ([Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AuditLog_Action_CreatedAt] ON [AuditLog] ([Action], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AuditLog_CreatedAt] ON [AuditLog] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AuditLog_Entity_EntityId] ON [AuditLog] ([Entity], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AuditLog_UserId_CreatedAt] ON [AuditLog] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Brand_Name] ON [Brand] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Brand_Slug] ON [Brand] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cart_Token] ON [Cart] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Cart_UpdatedAt] ON [Cart] ([UpdatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Cart_UserId] ON [Cart] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_CartItem_CartId] ON [CartItem] ([CartId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_CartItem_VariantId] ON [CartItem] ([VariantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Category_ParentId] ON [Category] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Category_Slug] ON [Category] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DataSubjectRequest_Email] ON [DataSubjectRequest] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DataSubjectRequest_PatientId] ON [DataSubjectRequest] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DataSubjectRequest_Status_CreatedAt] ON [DataSubjectRequest] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DataSubjectRequest_UserId] ON [DataSubjectRequest] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Frame_BrandId] ON [Frame] ([BrandId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Frame_SearchText] ON [Frame] ([SearchText]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Frame_Shape] ON [Frame] ([Shape]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Frame_Sku] ON [Frame] ([Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Frame_Slug] ON [Frame] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Frame_Status_BasePriceMinor] ON [Frame] ([Status], [BasePriceMinor]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Frame_Status_Gender_Shape] ON [Frame] ([Status], [Gender], [Shape]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Frame_Status_IsFeatured] ON [Frame] ([Status], [IsFeatured]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_FrameCategory_CategoryId] ON [FrameCategory] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_FrameVariant_Barcode] ON [FrameVariant] ([Barcode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_FrameVariant_FrameId_IsActive] ON [FrameVariant] ([FrameId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FrameVariant_Sku] ON [FrameVariant] ([Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_FrameVariant_StockQty] ON [FrameVariant] ([StockQty]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ImportJob_CreatedAt] ON [ImportJob] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ImportJob_Kind_Status] ON [ImportJob] ([Kind], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LensOption_Code] ON [LensOption] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_LensOption_Group_Position] ON [LensOption] ([Group], [Position]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_LensOption_IsActive] ON [LensOption] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_MediaAsset_CreatedAt] ON [MediaAsset] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_MediaAsset_DeletedAt] ON [MediaAsset] ([DeletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_MediaAsset_DeletedAt_PurgedAt] ON [MediaAsset] ([DeletedAt], [PurgedAt]) WHERE [DeletedAt] IS NOT NULL AND [PurgedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_MediaAsset_Filename] ON [MediaAsset] ([Filename]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_BillingAddressId] ON [Order] ([BillingAddressId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_Email] ON [Order] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Order_OrderNo] ON [Order] ([OrderNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_PatientId] ON [Order] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_PaymentStatus] ON [Order] ([PaymentStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_PlacedAt] ON [Order] ([PlacedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_PromotionId_UserId] ON [Order] ([PromotionId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_ShippingAddressId] ON [Order] ([ShippingAddressId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_Status_PlacedAt] ON [Order] ([Status], [PlacedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Order_UserId] ON [Order] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_OrderItem_LabStatus] ON [OrderItem] ([LabStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_OrderItem_OrderId] ON [OrderItem] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_OrderItem_PrescriptionId] ON [OrderItem] ([PrescriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_OrderItem_VariantId] ON [OrderItem] ([VariantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_OutboxEmail_RelatedEntity_RelatedEntityId] ON [OutboxEmail] ([RelatedEntity], [RelatedEntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_OutboxEmail_Status_NextAttemptAt] ON [OutboxEmail] ([Status], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Patient_DeletedAt] ON [Patient] ([DeletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Patient_Email] ON [Patient] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Patient_FileNo] ON [Patient] ([FileNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Patient_LastName_FirstName] ON [Patient] ([LastName], [FirstName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Patient_Phone] ON [Patient] ([Phone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Patient_UserId] ON [Patient] ([UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_PatientDocument_PatientId] ON [PatientDocument] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payment_IdempotencyKey] ON [Payment] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Payment_OrderId] ON [Payment] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Payment_ProviderRef] ON [Payment] ([ProviderRef]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Payment_Status] ON [Payment] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Prescription_PatientId_IssuedAt] ON [Prescription] ([PatientId], [IssuedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Prescription_Status] ON [Prescription] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ProductImage_VariantId_Position] ON [ProductImage] ([VariantId], [Position]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Promotion_Code] ON [Promotion] ([Code]) WHERE [Code] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Promotion_IsActive_StartsAt_EndsAt] ON [Promotion] ([IsActive], [StartsAt], [EndsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Promotion_Priority] ON [Promotion] ([Priority]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Setting_Group] ON [Setting] ([Group]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Shipment_OrderId] ON [Shipment] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Shipment_TrackingNumber] ON [Shipment] ([TrackingNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShippingRate_EffectiveFrom_EffectiveTo] ON [ShippingRate] ([EffectiveFrom], [EffectiveTo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShippingRate_IsActive_Country_Position] ON [ShippingRate] ([IsActive], [Country], [Position]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TryOnSession_CreatedAt] ON [TryOnSession] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TryOnSession_PatientId] ON [TryOnSession] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TryOnSession_UserId] ON [TryOnSession] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TryOnSnapshot_SessionId] ON [TryOnSnapshot] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TryOnSnapshot_VariantId] ON [TryOnSnapshot] ([VariantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824062009_InitialSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824062009_InitialSchema', N'10.0.11');
END;

COMMIT;
GO

