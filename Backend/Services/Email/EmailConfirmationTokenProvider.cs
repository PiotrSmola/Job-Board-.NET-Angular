using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Backend.Services.Email;

// Token potwierdzenia adresu e-mail z własnym czasem ważności (3 dni) - domyślny dostawca Identity (reset hasła)
// ma krótszy (1 h). Osobny "cel" ochrony danych: token jednego rodzaju nie zadziała jako drugi.
public sealed class EmailConfirmationTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
{
    public const string ProviderName = "EmailConfirmation";

    public EmailConfirmationTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<EmailConfirmationTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<TUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = "EmailConfirmationDataProtectorTokenProvider";
        TokenLifespan = TimeSpan.FromDays(3);
    }
}
