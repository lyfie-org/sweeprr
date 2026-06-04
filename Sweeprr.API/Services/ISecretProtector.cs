namespace Sweeprr.API.Services;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string? Unprotect(string ciphertext);
}
