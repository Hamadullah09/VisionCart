using Microsoft.EntityFrameworkCore;
using VisionCart.Application.Common;
using VisionCart.Application.Platform;
using VisionCart.Domain.Entities;

namespace VisionCart.Application.Accounts;

public sealed class AddressInput
{
    public string? Id { get; set; }
    public string? Label { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "PK";
    public bool IsDefault { get; set; }
}

public interface IAddressService
{
    Task<IReadOnlyList<Address>> ListAsync(string userId, CancellationToken ct = default);
    Task<Address?> FindAsync(string userId, string addressId, CancellationToken ct = default);
    Task<Address?> DefaultAsync(string userId, CancellationToken ct = default);
    Task<FormResult> SaveAsync(string userId, AddressInput input, CancellationToken ct = default);
    Task<ActionResult> DeleteAsync(string userId, string addressId, CancellationToken ct = default);
    Task<ActionResult> MakeDefaultAsync(string userId, string addressId, CancellationToken ct = default);
}

/// <summary>
/// The customer's address book.
///
/// Two rules shape this service. Addresses are **scoped to their owner on every
/// read**, never looked up by id alone — an address book keyed only by id is an
/// enumeration hole, and the ids are guessable enough to matter. And an address
/// is **never hard-deleted**: past orders reference the row they shipped to, so
/// removing one from the book must not erase where a delivered order went.
/// </summary>
public sealed class AddressService(IApplicationDbContext db, IAuditService audit) : IAddressService
{
    /// <summary>A book longer than this is a data-entry mistake, not a customer.</summary>
    private const int MaxPerCustomer = 20;

    public async Task<IReadOnlyList<Address>> ListAsync(string userId, CancellationToken ct = default) =>
        await db.Addresses
            .Where(a => a.UserId == userId && a.DeletedAt == null)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Label)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);

    public Task<Address?> FindAsync(string userId, string addressId, CancellationToken ct = default) =>
        db.Addresses.FirstOrDefaultAsync(
            a => a.Id == addressId && a.UserId == userId && a.DeletedAt == null, ct);

    public Task<Address?> DefaultAsync(string userId, CancellationToken ct = default) =>
        db.Addresses
            .Where(a => a.UserId == userId && a.DeletedAt == null)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<FormResult> SaveAsync(
        string userId, AddressInput input, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0) return FormResult.Fail("Please check the highlighted fields.", errors);

        Address address;
        bool isNew;

        if (!string.IsNullOrWhiteSpace(input.Id))
        {
            var existing = await FindAsync(userId, input.Id!, ct);
            if (existing is null) return FormResult.Fail("That address is no longer in your address book.");
            address = existing;
            isNew = false;
        }
        else
        {
            var count = await db.Addresses.CountAsync(
                a => a.UserId == userId && a.DeletedAt == null, ct);
            if (count >= MaxPerCustomer)
                return FormResult.Fail(
                    $"You can keep up to {MaxPerCustomer} addresses. Remove one before adding another.");

            address = new Address { UserId = userId };
            db.Addresses.Add(address);
            isNew = true;
        }

        address.Label = Trim(input.Label);
        address.FullName = input.FullName.Trim();
        address.Phone = Trim(input.Phone);
        address.Line1 = input.Line1.Trim();
        address.Line2 = Trim(input.Line2);
        address.City = input.City.Trim();
        address.State = Trim(input.State);
        address.PostalCode = Trim(input.PostalCode);
        address.Country = string.IsNullOrWhiteSpace(input.Country) ? "PK" : input.Country.Trim().ToUpperInvariant();

        // The first address a customer saves is their default whether they asked
        // for it or not; otherwise checkout would open with nothing selected.
        var hasOther = await db.Addresses.AnyAsync(
            a => a.UserId == userId && a.DeletedAt == null && a.Id != address.Id, ct);
        address.IsDefault = input.IsDefault || !hasOther;

        if (address.IsDefault) await ClearOtherDefaultsAsync(userId, address.Id, ct);

        await db.SaveChangesAsync(ct);

        // The detail carries the id only. An address is personal data, and §10
        // keeps it out of the audit trail even though staff may read it on the
        // patient record itself.
        await audit.WriteAsync(
            isNew ? AuditActions.AddressCreate : AuditActions.AddressUpdate,
            "Address", address.Id, null, ct);

        return FormResult.Success();
    }

    public async Task<ActionResult> DeleteAsync(
        string userId, string addressId, CancellationToken ct = default)
    {
        var address = await FindAsync(userId, addressId, ct);
        if (address is null) return ActionResult.Fail("That address is no longer in your address book.");

        address.DeletedAt = DateTime.UtcNow;
        address.IsDefault = false;

        // Removing the default leaves the book without one, and checkout would
        // then open empty for a customer who still has addresses. Promote the
        // next one rather than leaving that hole.
        var replacement = await db.Addresses
            .Where(a => a.UserId == userId && a.DeletedAt == null && a.Id != address.Id)
            .OrderBy(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (replacement is not null) replacement.IsDefault = true;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.AddressDelete, "Address", address.Id, null, ct);

        return ActionResult.Success();
    }

    public async Task<ActionResult> MakeDefaultAsync(
        string userId, string addressId, CancellationToken ct = default)
    {
        var address = await FindAsync(userId, addressId, ct);
        if (address is null) return ActionResult.Fail("That address is no longer in your address book.");

        await ClearOtherDefaultsAsync(userId, address.Id, ct);
        address.IsDefault = true;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.AddressUpdate, "Address", address.Id, null, ct);

        return ActionResult.Success();
    }

    private async Task ClearOtherDefaultsAsync(string userId, string keepId, CancellationToken ct)
    {
        var others = await db.Addresses
            .Where(a => a.UserId == userId && a.Id != keepId && a.IsDefault)
            .ToListAsync(ct);

        foreach (var other in others) other.IsDefault = false;
    }

    private static Dictionary<string, string> Validate(AddressInput input)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(input.FullName))
            errors[nameof(input.FullName)] = "Enter the name the courier should ask for.";
        if (string.IsNullOrWhiteSpace(input.Line1))
            errors[nameof(input.Line1)] = "Enter the street address.";
        if (string.IsNullOrWhiteSpace(input.City))
            errors[nameof(input.City)] = "Enter the city.";

        if (input.FullName.Length > 120) errors[nameof(input.FullName)] = "That name is too long.";
        if (input.Line1.Length > 200) errors[nameof(input.Line1)] = "That address line is too long.";

        return errors;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
