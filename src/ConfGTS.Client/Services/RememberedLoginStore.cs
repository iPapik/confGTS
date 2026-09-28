using Windows.Security.Credentials;

namespace ConfGTS.Client.Services;

internal sealed record RememberedLogin(string Username, string Password);

internal static class RememberedLoginStore
{
    private const string Resource = "ConfGTS.Client.Login";

    public static RememberedLogin? Load()
    {
        try
        {
            var vault = new PasswordVault();
            var credentials = vault.FindAllByResource(Resource);
            if (credentials.Count == 0)
                return null;

            var credential = credentials[0];
            credential.RetrievePassword();

            if (string.IsNullOrWhiteSpace(credential.UserName) ||
                string.IsNullOrEmpty(credential.Password))
            {
                return null;
            }

            return new RememberedLogin(credential.UserName, credential.Password);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string username, string password)
    {
        Clear();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return;

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(Resource, username.Trim(), password));
    }

    public static void Clear()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var credential in vault.FindAllByResource(Resource))
                vault.Remove(credential);
        }
        catch
        {
        }
    }
}
