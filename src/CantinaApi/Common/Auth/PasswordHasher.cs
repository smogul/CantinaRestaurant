using Microsoft.Extensions.Options;

namespace CantinaApi.Common.Auth;

public sealed class PasswordHasher(IOptions<AuthOptions> options)
{
    private readonly int _workFactor = options.Value.BcryptWorkFactor;
    private readonly Lazy<string> _dummyHash = new(() => BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString(), options.Value.BcryptWorkFactor));

    // The enhanced variant pre-hashes with SHA-384, so passwords longer than bcrypt's 72-byte limit still count in full.
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.EnhancedVerify(password, hash);

    // Burns the same time as a real check so response timing does not reveal whether an email is registered.
    public void VerifyAgainstDummyHash(string password) => BCrypt.Net.BCrypt.EnhancedVerify(password, _dummyHash.Value);
}
