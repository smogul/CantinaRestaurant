using Microsoft.Extensions.Options;

namespace CantinaApi.Common.Auth;

public sealed class PasswordHasher
{
    private readonly int _workFactor;

    // Hashed once with the configured work factor when the singleton is built, so a dummy check costs the same as a real one.
    private readonly string _dummyHash;

    public PasswordHasher(IOptions<AuthOptions> options)
    {
        _workFactor = options.Value.BcryptWorkFactor;
        _dummyHash = BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString(), _workFactor);
    }

    // The enhanced variant pre-hashes with SHA-384, so passwords longer than bcrypt's 72-byte limit still count in full.
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.EnhancedVerify(password, hash);

    // Burns the same time as a real check so response timing does not reveal whether an email is registered or locked.
    public void VerifyAgainstDummyHash(string password) => BCrypt.Net.BCrypt.EnhancedVerify(password, _dummyHash);
}
