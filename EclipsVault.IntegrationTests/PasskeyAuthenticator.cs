using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A disposable ES256 authenticator. Registration and sign-in go through the real HTTP ceremonies,
/// including challenge, origin, user-verification flags, signature, counter, persistence and auditing.
/// Only the authenticator is simulated; no vault service or authentication scheme is replaced.
/// </summary>
internal sealed class PasskeyAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _id = RandomNumberGenerator.GetBytes(32);
    private uint _counter;

    public object Register(JsonElement options)
    {
        var point = _key.ExportParameters(includePrivateParameters: false).Q;
        var cose = new CborWriter();
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2); // EC2
        cose.WriteInt32(3); cose.WriteInt32(-7); // ES256
        cose.WriteInt32(-1); cose.WriteInt32(1); // P-256
        cose.WriteInt32(-2); cose.WriteByteString(point.X!);
        cose.WriteInt32(-3); cose.WriteByteString(point.Y!);
        cose.WriteEndMap();

        var publicKey = cose.Encode();
        var authData = new byte[55 + _id.Length + publicKey.Length];
        AuthenticatorData(options.GetProperty("rp").GetProperty("id").GetString()!, 0x45).CopyTo(authData, 0);
        // Zero AAGUID, followed by the credential id length, id and COSE public key.
        BinaryPrimitives.WriteUInt16BigEndian(authData.AsSpan(53, 2), (ushort)_id.Length);
        _id.CopyTo(authData, 55);
        publicKey.CopyTo(authData, 55 + _id.Length);
        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("authData"); attestation.WriteByteString(authData);
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteEndMap();
        return new
        {
            id = Base64Url.EncodeToString(_id),
            clientDataJSON = Base64Url.EncodeToString(ClientData(options, "webauthn.create")),
            attestationObject = Base64Url.EncodeToString(attestation.Encode())
        };
    }

    public object Assert(JsonElement options)
    {
        _counter++;
        var authData = AuthenticatorData(options.GetProperty("rpId").GetString()!, 0x05);
        var clientData = ClientData(options, "webauthn.get");
        byte[] signedData = [.. authData, .. SHA256.HashData(clientData)];
        return new
        {
            id = Base64Url.EncodeToString(_id),
            clientDataJSON = Base64Url.EncodeToString(clientData),
            authenticatorData = Base64Url.EncodeToString(authData),
            signature = Base64Url.EncodeToString(_key.SignData(signedData, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
        };
    }

    private byte[] AuthenticatorData(string rpId, byte flags)
    {
        var data = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).CopyTo(data, 0);
        data[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(33), _counter);
        return data;
    }

    private static byte[] ClientData(JsonElement options, string type) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        type, challenge = options.GetProperty("challenge").GetString(), origin = "https://localhost:7443", crossOrigin = false
    });

    public void Dispose() => _key.Dispose();
}
