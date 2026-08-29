# Relationships

**38 foreign keys.**

| Delete behaviour | Count | Meaning |
| --- | --- | --- |
| `CASCADE` | 17 | Children of an owned aggregate — removed with the parent |
| `SET_NULL` | 12 | Optional reference — history survives the referent |
| `NO_ACTION` | 9 | **Protective** — the delete is refused |

---

| Table | Column | References | On delete |
| --- | --- | --- | --- |
| `Address` | `UserId` | `AspNetUsers` | NO_ACTION ⚠ |
| `Appointment` | `PatientId` | `Patient` | CASCADE |
| `Appointment` | `StaffUserId` | `AspNetUsers` | SET_NULL |
| `AspNetRoleClaims` | `RoleId` | `AspNetRoles` | CASCADE |
| `AspNetUserClaims` | `UserId` | `AspNetUsers` | CASCADE |
| `AspNetUserLogins` | `UserId` | `AspNetUsers` | CASCADE |
| `AspNetUserRoles` | `RoleId` | `AspNetRoles` | CASCADE |
| `AspNetUserRoles` | `UserId` | `AspNetUsers` | CASCADE |
| `AspNetUserTokens` | `UserId` | `AspNetUsers` | CASCADE |
| `AuditLog` | `UserId` | `AspNetUsers` | SET_NULL |
| `Cart` | `UserId` | `AspNetUsers` | SET_NULL |
| `CartItem` | `CartId` | `Cart` | CASCADE |
| `CartItem` | `VariantId` | `FrameVariant` | NO_ACTION ⚠ |
| `Category` | `ParentId` | `Category` | NO_ACTION ⚠ |
| `DataSubjectRequest` | `PatientId` | `Patient` | SET_NULL |
| `DataSubjectRequest` | `UserId` | `AspNetUsers` | SET_NULL |
| `Frame` | `BrandId` | `Brand` | SET_NULL |
| `FrameCategory` | `CategoryId` | `Category` | NO_ACTION ⚠ |
| `FrameCategory` | `FrameId` | `Frame` | CASCADE |
| `FrameVariant` | `FrameId` | `Frame` | CASCADE |
| `Order` | `BillingAddressId` | `Address` | NO_ACTION ⚠ |
| `Order` | `PatientId` | `Patient` | SET_NULL |
| `Order` | `PromotionId` | `Promotion` | SET_NULL |
| `Order` | `ShippingAddressId` | `Address` | NO_ACTION ⚠ |
| `Order` | `UserId` | `AspNetUsers` | SET_NULL |
| `OrderItem` | `OrderId` | `Order` | CASCADE |
| `OrderItem` | `PrescriptionId` | `Prescription` | NO_ACTION ⚠ |
| `OrderItem` | `VariantId` | `FrameVariant` | NO_ACTION ⚠ |
| `Patient` | `UserId` | `AspNetUsers` | SET_NULL |
| `PatientDocument` | `PatientId` | `Patient` | CASCADE |
| `Payment` | `OrderId` | `Order` | CASCADE |
| `Prescription` | `PatientId` | `Patient` | CASCADE |
| `ProductImage` | `VariantId` | `FrameVariant` | CASCADE |
| `Shipment` | `OrderId` | `Order` | CASCADE |
| `TryOnSession` | `PatientId` | `Patient` | SET_NULL |
| `TryOnSession` | `UserId` | `AspNetUsers` | SET_NULL |
| `TryOnSnapshot` | `SessionId` | `TryOnSession` | CASCADE |
| `TryOnSnapshot` | `VariantId` | `FrameVariant` | NO_ACTION ⚠ |

⚠ marks a protective relationship: the database refuses the delete.
