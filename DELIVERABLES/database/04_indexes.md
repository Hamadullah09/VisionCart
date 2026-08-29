# Indexes

**97 non-primary-key indexes**, of which **15 unique** and **6 filtered**.

A filtered unique index reproduces nullable-unique semantics: unique when present, but null many times over. Without the filter SQL Server treats two nulls as duplicates.

---

## Filtered unique indexes

| Table | Index | Filter |
| --- | --- | --- |
| `AspNetRoles` | `RoleNameIndex` | `([NormalizedName] IS NOT NULL)` |
| `AspNetUsers` | `UserNameIndex` | `([NormalizedUserName] IS NOT NULL)` |
| `MediaAsset` | `IX_MediaAsset_DeletedAt_PurgedAt` | `([DeletedAt] IS NOT NULL AND [PurgedAt] IS NULL)` |
| `Patient` | `IX_Patient_UserId` | `([UserId] IS NOT NULL)` |
| `Payment` | `IX_Payment_IdempotencyKey` | `([IdempotencyKey] IS NOT NULL)` |
| `Promotion` | `IX_Promotion_Code` | `([Code] IS NOT NULL)` |

## All indexes

| Table | Index | Kind |
| --- | --- | --- |
| `Address` | `IX_Address_UserId` | index |
| `Address` | `IX_Address_UserId_IsDefault` | index |
| `Appointment` | `IX_Appointment_PatientId_StartsAt` | index |
| `Appointment` | `IX_Appointment_StaffUserId_StartsAt` | index |
| `Appointment` | `IX_Appointment_StartsAt` | index |
| `Appointment` | `IX_Appointment_Status` | index |
| `AspNetRoleClaims` | `IX_AspNetRoleClaims_RoleId` | index |
| `AspNetRoles` | `RoleNameIndex` | UNIQUE |
| `AspNetUserClaims` | `IX_AspNetUserClaims_UserId` | index |
| `AspNetUserLogins` | `IX_AspNetUserLogins_UserId` | index |
| `AspNetUserRoles` | `IX_AspNetUserRoles_RoleId` | index |
| `AspNetUsers` | `EmailIndex` | index |
| `AspNetUsers` | `IX_AspNetUsers_IsActive` | index |
| `AspNetUsers` | `IX_AspNetUsers_Role` | index |
| `AspNetUsers` | `UserNameIndex` | UNIQUE |
| `AuditLog` | `IX_AuditLog_Action_CreatedAt` | index |
| `AuditLog` | `IX_AuditLog_CreatedAt` | index |
| `AuditLog` | `IX_AuditLog_Entity_EntityId` | index |
| `AuditLog` | `IX_AuditLog_UserId_CreatedAt` | index |
| `Brand` | `IX_Brand_Name` | UNIQUE |
| `Brand` | `IX_Brand_Slug` | UNIQUE |
| `Cart` | `IX_Cart_Token` | UNIQUE |
| `Cart` | `IX_Cart_UpdatedAt` | index |
| `Cart` | `IX_Cart_UserId` | index |
| `CartItem` | `IX_CartItem_CartId` | index |
| `CartItem` | `IX_CartItem_VariantId` | index |
| `Category` | `IX_Category_ParentId` | index |
| `Category` | `IX_Category_Slug` | UNIQUE |
| `DataSubjectRequest` | `IX_DataSubjectRequest_Email` | index |
| `DataSubjectRequest` | `IX_DataSubjectRequest_PatientId` | index |
| `DataSubjectRequest` | `IX_DataSubjectRequest_Status_CreatedAt` | index |
| `DataSubjectRequest` | `IX_DataSubjectRequest_UserId` | index |
| `Frame` | `IX_Frame_BrandId` | index |
| `Frame` | `IX_Frame_SearchText` | index |
| `Frame` | `IX_Frame_Shape` | index |
| `Frame` | `IX_Frame_Sku` | UNIQUE |
| `Frame` | `IX_Frame_Slug` | UNIQUE |
| `Frame` | `IX_Frame_Status_BasePriceMinor` | index |
| `Frame` | `IX_Frame_Status_Gender_Shape` | index |
| `Frame` | `IX_Frame_Status_IsFeatured` | index |
| `FrameCategory` | `IX_FrameCategory_CategoryId` | index |
| `FrameVariant` | `IX_FrameVariant_Barcode` | index |
| `FrameVariant` | `IX_FrameVariant_FrameId_IsActive` | index |
| `FrameVariant` | `IX_FrameVariant_Sku` | UNIQUE |
| `FrameVariant` | `IX_FrameVariant_StockQty` | index |
| `ImportJob` | `IX_ImportJob_CreatedAt` | index |
| `ImportJob` | `IX_ImportJob_Kind_Status` | index |
| `LensOption` | `IX_LensOption_Code` | UNIQUE |
| `LensOption` | `IX_LensOption_Group_Position` | index |
| `LensOption` | `IX_LensOption_IsActive` | index |
| `MediaAsset` | `IX_MediaAsset_CreatedAt` | index |
| `MediaAsset` | `IX_MediaAsset_DeletedAt` | index |
| `MediaAsset` | `IX_MediaAsset_DeletedAt_PurgedAt` | index |
| `MediaAsset` | `IX_MediaAsset_Filename` | index |
| `Order` | `IX_Order_BillingAddressId` | index |
| `Order` | `IX_Order_Email` | index |
| `Order` | `IX_Order_OrderNo` | UNIQUE |
| `Order` | `IX_Order_PatientId` | index |
| `Order` | `IX_Order_PaymentStatus` | index |
| `Order` | `IX_Order_PlacedAt` | index |
| `Order` | `IX_Order_PromotionId_UserId` | index |
| `Order` | `IX_Order_ShippingAddressId` | index |
| `Order` | `IX_Order_Status_PlacedAt` | index |
| `Order` | `IX_Order_UserId` | index |
| `OrderItem` | `IX_OrderItem_LabStatus` | index |
| `OrderItem` | `IX_OrderItem_OrderId` | index |
| `OrderItem` | `IX_OrderItem_PrescriptionId` | index |
| `OrderItem` | `IX_OrderItem_VariantId` | index |
| `OutboxEmail` | `IX_OutboxEmail_RelatedEntity_RelatedEntityId` | index |
| `OutboxEmail` | `IX_OutboxEmail_Status_NextAttemptAt` | index |
| `Patient` | `IX_Patient_DeletedAt` | index |
| `Patient` | `IX_Patient_Email` | index |
| `Patient` | `IX_Patient_FileNo` | UNIQUE |
| `Patient` | `IX_Patient_LastName_FirstName` | index |
| `Patient` | `IX_Patient_Phone` | index |
| `Patient` | `IX_Patient_UserId` | UNIQUE |
| `PatientDocument` | `IX_PatientDocument_PatientId` | index |
| `Payment` | `IX_Payment_IdempotencyKey` | UNIQUE |
| `Payment` | `IX_Payment_OrderId` | index |
| `Payment` | `IX_Payment_ProviderRef` | index |
| `Payment` | `IX_Payment_Status` | index |
| `Prescription` | `IX_Prescription_PatientId_IssuedAt` | index |
| `Prescription` | `IX_Prescription_Status` | index |
| `ProductImage` | `IX_ProductImage_VariantId_Position` | index |
| `Promotion` | `IX_Promotion_Code` | UNIQUE |
| `Promotion` | `IX_Promotion_IsActive_StartsAt_EndsAt` | index |
| `Promotion` | `IX_Promotion_Priority` | index |
| `Setting` | `IX_Setting_Group` | index |
| `Shipment` | `IX_Shipment_OrderId` | index |
| `Shipment` | `IX_Shipment_TrackingNumber` | index |
| `ShippingRate` | `IX_ShippingRate_EffectiveFrom_EffectiveTo` | index |
| `ShippingRate` | `IX_ShippingRate_IsActive_Country_Position` | index |
| `TryOnSession` | `IX_TryOnSession_CreatedAt` | index |
| `TryOnSession` | `IX_TryOnSession_PatientId` | index |
| `TryOnSession` | `IX_TryOnSession_UserId` | index |
| `TryOnSnapshot` | `IX_TryOnSnapshot_SessionId` | index |
| `TryOnSnapshot` | `IX_TryOnSnapshot_VariantId` | index |
