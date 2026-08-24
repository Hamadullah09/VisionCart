using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisionCart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Brand",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    About = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brand", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ParentId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Category_Category_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Category",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ImportJob",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Filename = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    OkRows = table.Column<int>(type: "int", nullable: false),
                    ErrorRows = table.Column<int>(type: "int", nullable: false),
                    Report = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDryRun = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJob", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LensOption",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PriceMinor = table.Column<int>(type: "int", nullable: false),
                    MinSphere = table.Column<double>(type: "float", nullable: true),
                    MaxSphere = table.Column<double>(type: "float", nullable: true),
                    MaxCylinder = table.Column<double>(type: "float", nullable: true),
                    Requires = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Excludes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LensOption", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAsset",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ThumbUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Filename = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<int>(type: "int", nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    UploadedBy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ThumbStorageKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    StorageProvider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurgeError = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PurgeAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAsset", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEmail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: false),
                    TextBody = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    Template = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RelatedEntity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RelatedEntityId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEmail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promotion",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    MaxDiscountMinor = table.Column<int>(type: "int", nullable: true),
                    MinSubtotalMinor = table.Column<int>(type: "int", nullable: false),
                    MinQty = table.Column<int>(type: "int", nullable: false),
                    BrandIds = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CategoryIds = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FrameIds = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FirstOrderOnly = table.Column<bool>(type: "bit", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsageLimit = table.Column<int>(type: "int", nullable: true),
                    UsageLimitPerUser = table.Column<int>(type: "int", nullable: true),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    Stackable = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BannerText = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    BannerColor = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Setting",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Setting", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ShippingRate",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MinSubtotalMinor = table.Column<int>(type: "int", nullable: false),
                    MaxSubtotalMinor = table.Column<int>(type: "int", nullable: true),
                    PriceMinor = table.Column<int>(type: "int", nullable: false),
                    EtaDaysMin = table.Column<int>(type: "int", nullable: false),
                    EtaDaysMax = table.Column<int>(type: "int", nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Address",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Line1 = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Line2 = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    City = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Address_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Cart",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PromoCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cart", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cart_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Patient",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    PdMm = table.Column<double>(type: "float", nullable: true),
                    PdNearMm = table.Column<double>(type: "float", nullable: true),
                    FaceMetrics = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ConsentMarketing = table.Column<bool>(type: "bit", nullable: false),
                    ConsentDataAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsentVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RetentionUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Patient_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Frame",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BrandId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    Shape = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RimType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FaceShapes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LensWidthMm = table.Column<double>(type: "float", nullable: true),
                    BridgeWidthMm = table.Column<double>(type: "float", nullable: true),
                    TempleLengthMm = table.Column<double>(type: "float", nullable: true),
                    LensHeightMm = table.Column<double>(type: "float", nullable: true),
                    TotalWidthMm = table.Column<double>(type: "float", nullable: true),
                    WeightGrams = table.Column<double>(type: "float", nullable: true),
                    SizeBand = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BasePriceMinor = table.Column<int>(type: "int", nullable: false),
                    CompareAtMinor = table.Column<int>(type: "int", nullable: true),
                    CostMinor = table.Column<int>(type: "int", nullable: true),
                    AllowFrameOnly = table.Column<bool>(type: "bit", nullable: false),
                    RequiresPrescription = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    MetaDesc = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SearchText = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Frame", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Frame_Brand_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brand",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Appointment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PatientId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StaffUserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointment_AspNetUsers_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Appointment_Patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DataSubjectRequest",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PatientId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerMessage = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    StaffNotes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    HandledByUserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    HandledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSubjectRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataSubjectRequest_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DataSubjectRequest_Patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Order",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PatientId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FulfilmentStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    SubtotalMinor = table.Column<int>(type: "int", nullable: false),
                    LensTotalMinor = table.Column<int>(type: "int", nullable: false),
                    DiscountMinor = table.Column<int>(type: "int", nullable: false),
                    ShippingMinor = table.Column<int>(type: "int", nullable: false),
                    TaxMinor = table.Column<int>(type: "int", nullable: false),
                    TotalMinor = table.Column<int>(type: "int", nullable: false),
                    PromoCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PromotionId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ShippingAddressId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BillingAddressId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    InternalNotes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Order", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Order_Address_BillingAddressId",
                        column: x => x.BillingAddressId,
                        principalTable: "Address",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Order_Address_ShippingAddressId",
                        column: x => x.ShippingAddressId,
                        principalTable: "Address",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Order_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Order_Patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Order_Promotion_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PatientDocument",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PatientId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientDocument_Patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prescription",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PatientId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Prescriber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Clinic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OdSphere = table.Column<double>(type: "float", nullable: true),
                    OdCylinder = table.Column<double>(type: "float", nullable: true),
                    OdAxis = table.Column<int>(type: "int", nullable: true),
                    OdAdd = table.Column<double>(type: "float", nullable: true),
                    OdPrism = table.Column<double>(type: "float", nullable: true),
                    OdPrismBase = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    OdPdMm = table.Column<double>(type: "float", nullable: true),
                    OsSphere = table.Column<double>(type: "float", nullable: true),
                    OsCylinder = table.Column<double>(type: "float", nullable: true),
                    OsAxis = table.Column<int>(type: "int", nullable: true),
                    OsAdd = table.Column<double>(type: "float", nullable: true),
                    OsPrism = table.Column<double>(type: "float", nullable: true),
                    OsPrismBase = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    OsPdMm = table.Column<double>(type: "float", nullable: true),
                    OdSegHeightMm = table.Column<double>(type: "float", nullable: true),
                    OsSegHeightMm = table.Column<double>(type: "float", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prescription_Patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TryOnSession",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PatientId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PhotoUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    FaceData = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TryOnSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TryOnSession_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TryOnSession_Patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FrameCategory",
                columns: table => new
                {
                    FrameId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrameCategory", x => new { x.FrameId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_FrameCategory_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FrameCategory_Frame_FrameId",
                        column: x => x.FrameId,
                        principalTable: "Frame",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FrameVariant",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FrameId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ColorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ColorHex = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PriceMinor = table.Column<int>(type: "int", nullable: true),
                    StockQty = table.Column<int>(type: "int", nullable: false),
                    LowStockAt = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    TryOnImageUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AnchorLeftX = table.Column<double>(type: "float", nullable: false),
                    AnchorLeftY = table.Column<double>(type: "float", nullable: false),
                    AnchorRightX = table.Column<double>(type: "float", nullable: false),
                    AnchorRightY = table.Column<double>(type: "float", nullable: false),
                    TryOnScaleAdj = table.Column<double>(type: "float", nullable: false),
                    TryOnOpacity = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrameVariant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrameVariant_Frame_FrameId",
                        column: x => x.FrameId,
                        principalTable: "Frame",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AmountMinor = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ProviderRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RefundedMinor = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payment_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shipment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TrackingUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LabelUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CostMinor = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProviderRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipment_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartItem",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CartId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VariantId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    LensOptionCodes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    PrescriptionDraft = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    PrescriptionId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TryOnSnapshotId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UnitPriceMinor = table.Column<int>(type: "int", nullable: false),
                    LensPriceMinor = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartItem_Cart_CartId",
                        column: x => x.CartId,
                        principalTable: "Cart",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItem_FrameVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "FrameVariant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrderItem",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VariantId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TitleSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ImageSnapshot = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitPriceMinor = table.Column<int>(type: "int", nullable: false),
                    LensPriceMinor = table.Column<int>(type: "int", nullable: false),
                    TotalMinor = table.Column<int>(type: "int", nullable: false),
                    LensOptionCodes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LensSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PrescriptionId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PrescriptionSnapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    TryOnSnapshotUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LabStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LabRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItem_FrameVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "FrameVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItem_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItem_Prescription_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImage",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VariantId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ThumbUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Alt = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImage_FrameVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "FrameVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TryOnSnapshot",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VariantId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TryOnSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TryOnSnapshot_FrameVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "FrameVariant",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TryOnSnapshot_TryOnSession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TryOnSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Address_UserId",
                table: "Address",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Address_UserId_IsDefault",
                table: "Address",
                columns: new[] { "UserId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointment_PatientId_StartsAt",
                table: "Appointment",
                columns: new[] { "PatientId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointment_StaffUserId_StartsAt",
                table: "Appointment",
                columns: new[] { "StaffUserId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointment_StartsAt",
                table: "Appointment",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Appointment_Status",
                table: "Appointment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsActive",
                table: "AspNetUsers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Role",
                table: "AspNetUsers",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Action_CreatedAt",
                table: "AuditLog",
                columns: new[] { "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_CreatedAt",
                table: "AuditLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Entity_EntityId",
                table: "AuditLog",
                columns: new[] { "Entity", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId_CreatedAt",
                table: "AuditLog",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Brand_Name",
                table: "Brand",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brand_Slug",
                table: "Brand",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cart_Token",
                table: "Cart",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cart_UpdatedAt",
                table: "Cart",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Cart_UserId",
                table: "Cart",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItem_CartId",
                table: "CartItem",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItem_VariantId",
                table: "CartItem",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Category_ParentId",
                table: "Category",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Category_Slug",
                table: "Category",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequest_Email",
                table: "DataSubjectRequest",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequest_PatientId",
                table: "DataSubjectRequest",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequest_Status_CreatedAt",
                table: "DataSubjectRequest",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequest_UserId",
                table: "DataSubjectRequest",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Frame_BrandId",
                table: "Frame",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Frame_SearchText",
                table: "Frame",
                column: "SearchText");

            migrationBuilder.CreateIndex(
                name: "IX_Frame_Shape",
                table: "Frame",
                column: "Shape");

            migrationBuilder.CreateIndex(
                name: "IX_Frame_Sku",
                table: "Frame",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Frame_Slug",
                table: "Frame",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Frame_Status_BasePriceMinor",
                table: "Frame",
                columns: new[] { "Status", "BasePriceMinor" });

            migrationBuilder.CreateIndex(
                name: "IX_Frame_Status_Gender_Shape",
                table: "Frame",
                columns: new[] { "Status", "Gender", "Shape" });

            migrationBuilder.CreateIndex(
                name: "IX_Frame_Status_IsFeatured",
                table: "Frame",
                columns: new[] { "Status", "IsFeatured" });

            migrationBuilder.CreateIndex(
                name: "IX_FrameCategory_CategoryId",
                table: "FrameCategory",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FrameVariant_Barcode",
                table: "FrameVariant",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_FrameVariant_FrameId_IsActive",
                table: "FrameVariant",
                columns: new[] { "FrameId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FrameVariant_Sku",
                table: "FrameVariant",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrameVariant_StockQty",
                table: "FrameVariant",
                column: "StockQty");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJob_CreatedAt",
                table: "ImportJob",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJob_Kind_Status",
                table: "ImportJob",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LensOption_Code",
                table: "LensOption",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LensOption_Group_Position",
                table: "LensOption",
                columns: new[] { "Group", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_LensOption_IsActive",
                table: "LensOption",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAsset_CreatedAt",
                table: "MediaAsset",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAsset_DeletedAt",
                table: "MediaAsset",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAsset_DeletedAt_PurgedAt",
                table: "MediaAsset",
                columns: new[] { "DeletedAt", "PurgedAt" },
                filter: "[DeletedAt] IS NOT NULL AND [PurgedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAsset_Filename",
                table: "MediaAsset",
                column: "Filename");

            migrationBuilder.CreateIndex(
                name: "IX_Order_BillingAddressId",
                table: "Order",
                column: "BillingAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Order_Email",
                table: "Order",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Order_OrderNo",
                table: "Order",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Order_PatientId",
                table: "Order",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Order_PaymentStatus",
                table: "Order",
                column: "PaymentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Order_PlacedAt",
                table: "Order",
                column: "PlacedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Order_PromotionId_UserId",
                table: "Order",
                columns: new[] { "PromotionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Order_ShippingAddressId",
                table: "Order",
                column: "ShippingAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Order_Status_PlacedAt",
                table: "Order",
                columns: new[] { "Status", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Order_UserId",
                table: "Order",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_LabStatus",
                table: "OrderItem",
                column: "LabStatus");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_OrderId",
                table: "OrderItem",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_PrescriptionId",
                table: "OrderItem",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_VariantId",
                table: "OrderItem",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmail_RelatedEntity_RelatedEntityId",
                table: "OutboxEmail",
                columns: new[] { "RelatedEntity", "RelatedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmail_Status_NextAttemptAt",
                table: "OutboxEmail",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Patient_DeletedAt",
                table: "Patient",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Patient_Email",
                table: "Patient",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Patient_FileNo",
                table: "Patient",
                column: "FileNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patient_LastName_FirstName",
                table: "Patient",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Patient_Phone",
                table: "Patient",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_Patient_UserId",
                table: "Patient",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocument_PatientId",
                table: "PatientDocument",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_IdempotencyKey",
                table: "Payment",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_OrderId",
                table: "Payment",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_ProviderRef",
                table: "Payment",
                column: "ProviderRef");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_Status",
                table: "Payment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Prescription_PatientId_IssuedAt",
                table: "Prescription",
                columns: new[] { "PatientId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescription_Status",
                table: "Prescription",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_VariantId_Position",
                table: "ProductImage",
                columns: new[] { "VariantId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_Code",
                table: "Promotion",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_IsActive_StartsAt_EndsAt",
                table: "Promotion",
                columns: new[] { "IsActive", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_Priority",
                table: "Promotion",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Setting_Group",
                table: "Setting",
                column: "Group");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_OrderId",
                table: "Shipment",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipment_TrackingNumber",
                table: "Shipment",
                column: "TrackingNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRate_EffectiveFrom_EffectiveTo",
                table: "ShippingRate",
                columns: new[] { "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRate_IsActive_Country_Position",
                table: "ShippingRate",
                columns: new[] { "IsActive", "Country", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_TryOnSession_CreatedAt",
                table: "TryOnSession",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TryOnSession_PatientId",
                table: "TryOnSession",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_TryOnSession_UserId",
                table: "TryOnSession",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TryOnSnapshot_SessionId",
                table: "TryOnSnapshot",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TryOnSnapshot_VariantId",
                table: "TryOnSnapshot",
                column: "VariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointment");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "CartItem");

            migrationBuilder.DropTable(
                name: "DataSubjectRequest");

            migrationBuilder.DropTable(
                name: "FrameCategory");

            migrationBuilder.DropTable(
                name: "ImportJob");

            migrationBuilder.DropTable(
                name: "LensOption");

            migrationBuilder.DropTable(
                name: "MediaAsset");

            migrationBuilder.DropTable(
                name: "OrderItem");

            migrationBuilder.DropTable(
                name: "OutboxEmail");

            migrationBuilder.DropTable(
                name: "PatientDocument");

            migrationBuilder.DropTable(
                name: "Payment");

            migrationBuilder.DropTable(
                name: "ProductImage");

            migrationBuilder.DropTable(
                name: "Setting");

            migrationBuilder.DropTable(
                name: "Shipment");

            migrationBuilder.DropTable(
                name: "ShippingRate");

            migrationBuilder.DropTable(
                name: "TryOnSnapshot");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Cart");

            migrationBuilder.DropTable(
                name: "Category");

            migrationBuilder.DropTable(
                name: "Prescription");

            migrationBuilder.DropTable(
                name: "Order");

            migrationBuilder.DropTable(
                name: "FrameVariant");

            migrationBuilder.DropTable(
                name: "TryOnSession");

            migrationBuilder.DropTable(
                name: "Address");

            migrationBuilder.DropTable(
                name: "Promotion");

            migrationBuilder.DropTable(
                name: "Frame");

            migrationBuilder.DropTable(
                name: "Patient");

            migrationBuilder.DropTable(
                name: "Brand");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
