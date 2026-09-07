/* ---------------------------------------------------------------------------
   Runbook: seeding cannot change an existing account's password
   ---------------------------------------------------------------------------
   Run in the host's SQL manager (myASP.NET: DATABASES -> MSSQL -> <database>
   -> Webconnect -> Tools -> Run Query), against the application's database.

   The trap
   --------
   Seed__AdminEmail only ever CREATES. DatabaseSeeder.EnsureUserAsync looks the
   address up first and, if it finds an account, adds the missing role and
   returns -- without touching the password:

       var existing = await users.FindByEmailAsync(email);
       if (existing is not null)
       {
           if (!await users.IsInRoleAsync(existing, role))
               await users.AddToRoleAsync(existing, role);
           return;                       // password is never set
       }

   So pointing Seed__AdminEmail at an address somebody already registered on the
   storefront silently grants that account admin while leaving its original
   password in place. The seed log is how you tell: a created account logs
   "Created seed admin account <email>", a skipped one logs nothing at all, and
   a rejected one logs "Could not create seed user". No line means it existed.

   Why you cannot just reset the password instead
   ----------------------------------------------
   /forgot-password emails a link, and the outbox stores
   TextBody = EmailTemplates.ToPlainText(htmlBody). That strips every tag with
   <[^>]+>, and the link lives in an <a href>. So when Email:Driver is 'log' the
   logged copy of the mail does NOT contain the URL, and the link cannot be
   recovered from the log. With no SMTP host configured there is no other copy.

   The fix below frees the address by RENAMING the colliding account rather than
   deleting it, so orders, addresses and audit history stay attached and the
   change is reversible. Re-seed afterwards and the account is created properly.

   Replace <colliding-address> and <old-admin-address> before running.
--------------------------------------------------------------------------- */


/* 1 -- look before you touch -------------------------------------------- */
SELECT   Email, Name, Role, IsActive, EmailConfirmed, LockoutEnd
FROM     AspNetUsers
ORDER BY Role, Email;


/* 2 -- free the address by renaming the account that holds it ------------
       All four columns must move together: Identity looks accounts up by the
       Normalized* pair, and UserName is the sign-in identity. */
UPDATE AspNetUsers
SET    Email              = 'former-<colliding-address>',
       NormalizedEmail    = 'FORMER-<COLLIDING-ADDRESS>',
       UserName           = 'former-<colliding-address>',
       NormalizedUserName = 'FORMER-<COLLIDING-ADDRESS>',
       IsActive           = 0
WHERE  NormalizedEmail = '<COLLIDING-ADDRESS>';


/* 3 -- retire a superseded administrator --------------------------------
       IsActive = 0 is checked in AccountController before the password is even
       verified, and again in the /forgot-password handler, so the account can
       neither sign in nor be recovered by its owner. History is preserved.

       Do this only AFTER confirming you can sign in as the new administrator:
       disabling the last account you can actually log in as leaves SQL as the
       only way back. */
UPDATE AspNetUsers
SET    IsActive = 0
WHERE  NormalizedEmail = '<OLD-ADMIN-ADDRESS>';


/* 4 -- confirm ----------------------------------------------------------- */
SELECT   Email, Role, IsActive
FROM     AspNetUsers
WHERE    Role IN ('admin', 'optician', 'staff')
ORDER BY Role, Email;


/* ---------------------------------------------------------------------------
   Undo
   UPDATE AspNetUsers
   SET    Email = '<colliding-address>', NormalizedEmail = '<COLLIDING-ADDRESS>',
          UserName = '<colliding-address>', NormalizedUserName = '<COLLIDING-ADDRESS>',
          IsActive = 1
   WHERE  NormalizedEmail = 'FORMER-<COLLIDING-ADDRESS>';

   UPDATE AspNetUsers SET IsActive = 1 WHERE NormalizedEmail = '<OLD-ADMIN-ADDRESS>';

   Then set Role correctly if the account was promoted rather than created:
   authorisation reads AspNetUserRoles, but AspNetUsers.Role is what the login
   audit entry and the GDPR export report.
--------------------------------------------------------------------------- */
