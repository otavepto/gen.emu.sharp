using SteamKit2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using SteamKit2.Authentication;
using System.Globalization;
using common.utils;

namespace gen.emu.cfg.SteamNetwork;

public class Auth
{
  private static Auth? _instance;
  public static Auth Instance => _instance ??= new Auth();

  const string CRED_FILENAME_GUARD_DATA = "guard";
  const string CRED_FILENAME_REFRESH_TOKEN = "refresh_tk";
  const string CRED_FILENAME_ACCOUNT_NAME = "acname";

  const string SALT = "9enFFFe40FFFcf9"; // gen.emu.cfg
  const string CRED_FILE_PASSWORD =
    @"g9qg6DEamBB8zzjMSnDSeUQcd6599hLwv0rEDtJGRDo53fhoDLW0EVKFvTWXkfXQ9aMqOwLCDNBN3qhT3mGAsBOapnUxU6XioYiaL";

  const string CREDS_FOLDER_NAME = @"credentials";

  // when this is true, the Steam Guard JWT and the refresh token/JWT (which are stored for later logins) will expire in ~7 months
  // Steam Guard JWT is used in later logins to avoid triggering the email/2FA again
  // refresh token/JWT is used in later logins as a substitute for the password, so the user doesn't have re-enter it again
  const bool PERSISTENT_LOGIN = true;
  const int MAX_LOGIN_ATTEMPTS = 3;

  string creds_base_dir = CREDS_FOLDER_NAME;

  readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(10);

  TaskCompletionSource<SteamUser.LoggedOnCallback> activeLogin = new();
  TaskCompletionSource activeDisconnect = new();
  
  bool isRunning;
  bool attemptingConnection;
  TimeSpan reconnectDelay;
  int loginAttempts;

  SteamUser steamUser = default!;

  string? username;
  string? password;
  bool anonUser;

  string? storedRefreshToken;
  string? storedAccountName;
  string? storedGuardData;


  public SteamUser GetSteamUser => isRunning ? steamUser : throw new InvalidOperationException("Not logged in");


  public void Init(string baseFolder)
  {
    if (isRunning)
    {
      return;
    }

    creds_base_dir = Path.Combine(baseFolder, CREDS_FOLDER_NAME);

    TryReadCredFile(CRED_FILENAME_REFRESH_TOKEN, out storedRefreshToken);
    TryReadCredFile(CRED_FILENAME_ACCOUNT_NAME, out storedAccountName);
    TryReadCredFile(CRED_FILENAME_GUARD_DATA, out storedGuardData);
  }

  public async Task<SteamUser.LoggedOnCallback> LoginAsync(string? username, string? password, bool anon = false)
  {
    if (isRunning)
    {
      return await activeLogin.Task.ConfigureAwait(false);
    }

    ResetState();

    isRunning = true;
    attemptingConnection = true;

    this.username = username;
    this.password = password;
    anonUser = anon;

    // get the steamuser handler, which is used for logging on after successfully connecting
    steamUser = Client.Instance.GetSteamClient.GetHandler<SteamUser>() ?? throw new NullReferenceException("Couldn't get SteamUser instance");

    // create our callback handling loop
    _ = Task.Run(() =>
    {
      while (isRunning)
      {
        // in order for the callbacks to get routed, they need to be handled by the manager
        Client.Instance.GetCallbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
      }
    });

    await Task.Yield();

    // register a few callbacks we're interested in
    // these are registered upon creation to a callback manager, which will then route the callbacks
    // to the functions specified
    Client.Instance.GetCallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
    Client.Instance.GetCallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

    Client.Instance.GetCallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
    Client.Instance.GetCallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

    // initiate the connection
    Client.Instance.GetSteamClient.Connect();

    return await activeLogin.Task.ConfigureAwait(false);
  }

  public Task ShutdownAsync()
  {
    if (!isRunning)
    {
      return Task.CompletedTask;
    }

    attemptingConnection = false;
    loginAttempts = MAX_LOGIN_ATTEMPTS;
    steamUser?.LogOff();
    Client.Instance.GetSteamClient.Disconnect();
    return activeDisconnect.Task;
  }


  void ResetState()
  {
    activeLogin = new();
    activeDisconnect = new();

    isRunning = false;
    attemptingConnection = false;

    // connection to steam takes a consistent amount of **trials** not seconds!
    // around 4-6 times, it won't matter if we started with a big delay or a small delay
    // in either cases we have to try 4-6 times
    reconnectDelay = TimeSpan.FromMilliseconds(10);

    loginAttempts = 0;

    steamUser = null!;

    username = null;
    password = null;
    anonUser = false;
  }


  string EncryptString(string inputString, string password)
  {
    // generate a Key and initialization vector from the password
    using var aes = Aes.Create();
    // key size of 256 bits
    aes.KeySize = 256;
    aes.BlockSize = 128;

    using (var keyGenerator = new Rfc2898DeriveBytes(
        password,
        Encoding.UTF8.GetBytes(SALT),
        10000, // iterations count
        HashAlgorithmName.SHA512))
    {
      aes.Key = keyGenerator.GetBytes(aes.KeySize / 8);
      aes.IV = keyGenerator.GetBytes(aes.BlockSize / 8);
    }

    using var memoryStream = new MemoryStream();
    // write the initialization vector
    memoryStream.Write(aes.IV, 0, aes.IV.Length);

    using var cryptoStream = new CryptoStream(memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
    var strBytes = Encoding.UTF8.GetBytes(inputString);
    var compressedData = Utils.CompressData(strBytes);

    // write the compressed data length first so we know how to read back
    var lengthPrefix = BitConverter.GetBytes(compressedData.Length);
    cryptoStream.Write(lengthPrefix, 0, lengthPrefix.Length);
    // write the compressed data
    cryptoStream.Write(compressedData, 0, compressedData.Length);
    // finalize encryption
    cryptoStream.FlushFinalBlock();

    return Convert.ToBase64String(memoryStream.ToArray());
  }

  string DecryptString(string encryptedCompressedDataBase64, string password)
  {
    // generate a Key and initialization vector from the password
    using var aes = Aes.Create();
    // key size of 256 bits
    aes.KeySize = 256;
    aes.BlockSize = 128;

    using (var keyGenerator = new Rfc2898DeriveBytes(
        password,
        Encoding.UTF8.GetBytes(SALT),
        10000, // iterations count
        HashAlgorithmName.SHA512))
    {
      aes.Key = keyGenerator.GetBytes(aes.KeySize / 8);
    }

    var encryptedCompressedData = Convert.FromBase64String(encryptedCompressedDataBase64);
    using var memoryStream = new MemoryStream(encryptedCompressedData);
    // read the IV first
    byte[] iv = new byte[aes.BlockSize / 8];
    for (int idx = 0; idx < iv.Length;)
    {
      var bytesRead = memoryStream.Read(iv, idx, iv.Length - idx);
      if (bytesRead == 0)
      {
        throw new InvalidOperationException($"Invalid IV length: expected={iv.Length}, read={idx}");
      }
      idx += bytesRead;
    }
    aes.IV = iv;

    using var cryptoStream = new CryptoStream(memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Read);

    // read length of compressed data
    byte[] lengthPrefix = new byte[sizeof(int)];
    for (int idx = 0; idx < lengthPrefix.Length;)
    {
      var bytesRead = cryptoStream.Read(lengthPrefix, idx, lengthPrefix.Length - idx);
      if (bytesRead == 0)
      {
        throw new InvalidOperationException($"Invalid data length prefix: expected={lengthPrefix.Length}, read={idx}");
      }
      idx += bytesRead;
    }
    int compressedDataLength = BitConverter.ToInt32(lengthPrefix, 0);

    // read compressed data
    byte[] compressedData = new byte[compressedDataLength];
    for (int idx = 0; idx < compressedData.Length;)
    {
      var bytesRead = cryptoStream.Read(compressedData, idx, compressedData.Length - idx);
      if (bytesRead == 0)
      {
        throw new InvalidOperationException($"Invalid data length: expected={compressedData.Length}, read={idx}");
      }
      idx += bytesRead;
    }

    var data = Utils.DecompressData(compressedData);
    return Encoding.UTF8.GetString(data);
  }

  void TryReadCredFile(string filename, out string? result)
  {
    try
    {
      var credFilepath = Path.Combine(creds_base_dir, filename);
      if (!File.Exists(credFilepath))
      {
        result = null;
        return;
      }

      result = DecryptString(File.ReadAllText(credFilepath), $"{CRED_FILE_PASSWORD}/{filename}");
    }
    catch
    {
      result = null;
    }
  }

  void TryWriteCredFile(string filename, string? data)
  {
    if (string.IsNullOrEmpty(data))
    {
      return;
    }

    try
    {
      Directory.CreateDirectory(creds_base_dir);
      var credFilepath = Path.Combine(creds_base_dir, filename);
      var result = EncryptString(data, $"{CRED_FILE_PASSWORD}/{filename}");
      File.WriteAllText(credFilepath, result, Utils.Utf8EncodingNoBom);
    }
    catch
    {

    }
  }



  async Task<AuthPollResult?> AttemptCleanLogin(string? guardData)
  {
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
    {
      throw new InvalidOperationException("Empty username or password");
    }

    // Begin authenticating via credentials
    var authSession = await Client.Instance.GetSteamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
    {
      Username = username,
      Password = password,
      IsPersistentSession = PERSISTENT_LOGIN,

      // See NewGuardData comment below
      GuardData = guardData,

      /// <see cref="UserConsoleAuthenticator"/> is the default authenticator implemention provided by SteamKit
      /// for ease of use which blocks the thread and asks for user input to enter the code.
      /// However, if you require special handling (e.g. you have the TOTP secret and can generate codes on the fly),
      /// you can implement your own <see cref="SteamKit2.Authentication.IAuthenticator"/>.
      Authenticator = new UserConsoleAuthenticator(),
    }).ConfigureAwait(false);

    // Starting polling Steam for authentication response
    var pollResponse = await authSession.PollingWaitForResultAsync().ConfigureAwait(false);
    return pollResponse;
  }

  void StoreNewLoginData(AuthPollResult? pollResponse)
  {
    if (pollResponse is null)
    {
      throw new InvalidOperationException("Empty auth response");
    }

    if (!string.IsNullOrEmpty(pollResponse.NewGuardData))
    {
      // When using certain two factor methods (such as email 2fa), guard data may be provided by Steam
      // for use in future authentication sessions to avoid triggering 2FA again (this works similarly to the old sentry file system).
      // Do note that this guard data is also a JWT token and has an expiration date.
      storedGuardData = pollResponse.NewGuardData;
      TryWriteCredFile(CRED_FILENAME_GUARD_DATA, storedGuardData);
    }

    if (!string.IsNullOrEmpty(pollResponse.RefreshToken))
    {
      storedRefreshToken = pollResponse.RefreshToken;
      TryWriteCredFile(CRED_FILENAME_REFRESH_TOKEN, storedRefreshToken);
    }

    if (!string.IsNullOrEmpty(pollResponse.AccountName))
    {
      storedAccountName = pollResponse.AccountName;
      TryWriteCredFile(CRED_FILENAME_ACCOUNT_NAME, storedAccountName);
    }
    else if (!string.IsNullOrEmpty(username))
    {
      storedAccountName = username;
      TryWriteCredFile(CRED_FILENAME_ACCOUNT_NAME, storedAccountName);
    }
  }



  void OnConnected(SteamClient.ConnectedCallback callback)
  {
    attemptingConnection = false;

    if (anonUser)
    {
      AnonLogin();
    }
    else
    {
      Task.Run(RealLogin);
    }
  }

  void AnonLogin()
  {
    steamUser.LogOnAnonymous(new SteamUser.AnonymousLogOnDetails
    {
      CellID = Client.Instance.GetCellId,
    });
  }

  async Task RealLogin()
  {
    if (string.IsNullOrEmpty(storedRefreshToken)) // if we don't have a previous refresh token to use for later logins
    {
      try
      {
        var pollResponse = await AttemptCleanLogin(storedGuardData).ConfigureAwait(false);
        StoreNewLoginData(pollResponse);
      }
      catch (Exception e)
      {
        activeLogin.TrySetException(e);
        attemptingConnection = false;
        loginAttempts = MAX_LOGIN_ATTEMPTS;
        Client.Instance.GetSteamClient.Disconnect();
        return;
      }
    }

    if (string.IsNullOrEmpty(storedAccountName))
    {
      if (string.IsNullOrEmpty(username)) // if we also don't have a username
      {
        activeLogin.TrySetException(new InvalidOperationException("Stored username is empty"));
        attemptingConnection = false;
        loginAttempts = MAX_LOGIN_ATTEMPTS;
        Client.Instance.GetSteamClient.Disconnect();
        return;
      }

      storedAccountName = username;
      TryWriteCredFile(CRED_FILENAME_ACCOUNT_NAME, storedAccountName);
    }

    // parse the JWT access token to see the scope and expiration date.
    //ParseJsonWebToken(storedRefreshToken, nameof(storedRefreshToken));
    //ParseJsonWebToken(storedGuardData, nameof(storedGuardData));

    // Logon to Steam with the access token we have received
    // Note that we are using RefreshToken for logging on here
    steamUser.LogOn(new SteamUser.LogOnDetails
    {
      Username = storedAccountName,
      AccessToken = storedRefreshToken,
      ShouldRememberPassword = PERSISTENT_LOGIN, // If you set IsPersistentSession to true, this also must be set to true for it to work correctly
    });
  }

  void OnLoggedOn(SteamUser.LoggedOnCallback callback)
  {
    loginAttempts++;

    if (callback.Result != EResult.OK)
    {
      if (callback.Result == EResult.TryAnotherCM) // timeout for 2FA/email auth
      {
        activeLogin.TrySetException(new InvalidOperationException($"Unable to login (timeout), result={callback.Result}, extended result={callback.ExtendedResult}"));
        attemptingConnection = false;
        loginAttempts = MAX_LOGIN_ATTEMPTS;
        Client.Instance.GetSteamClient.Disconnect();
      }

      // OnDisconnected callback will be triggered and a clean login attempt will be made
      return;
    }

    // at this point, we'd be able to perform actions on Steam

    // save the current cellid somewhere. if we lose our saved server list, we can use this when retrieving
    // servers from the Steam Directory.
    Client.Instance.TryWriteLastCellId(callback.CellID);

    activeLogin.TrySetResult(callback);
  }

  void OnLoggedOff(SteamUser.LoggedOffCallback callback)
  {
    //Console.WriteLine($"Logged off: {callback.Result}");
  }

  async void OnDisconnected(SteamClient.DisconnectedCallback callback)
  {
    if (!attemptingConnection)
    {
      if (loginAttempts < MAX_LOGIN_ATTEMPTS)
      {
        // next attempts are done with a clean state in case we have bad credentials from last attempt
        if (!anonUser)
        {
          storedRefreshToken = null;
          storedAccountName = null;
          storedGuardData = null;
        }
        attemptingConnection = true;
        // initiate the connection again
        Client.Instance.GetSteamClient.Connect();
      }
      else // we consumed all available login attempts
      {
        isRunning = false;
        activeDisconnect.TrySetResult();
        activeLogin.TrySetException(new InvalidOperationException($"Disconnected"));
      }

      return;
    }

    // See the note in ResetState(), uncomment this line to check the timing vs attempts
    //Console.WriteLine($"{DateTime.Now} connecting in {reconnectDelay} ...");
    await Task.Delay(reconnectDelay).ConfigureAwait(false);
    reconnectDelay *= 1.5;
    if (reconnectDelay > MaxReconnectDelay)
    {
      reconnectDelay = MaxReconnectDelay;
    }
    Client.Instance.GetSteamClient.Connect();
  }



  // This is simply showing how to parse JWT, this is not required to login to Steam
  void ParseJsonWebToken(string? token, string name)
  {
    if (string.IsNullOrEmpty(token))
    {
      return;
    }

    // You can use a JWT library to do the parsing for you
    var tokenComponents = token.Split('.');

    // Fix up base64url to normal base64
    var base64 = tokenComponents[1].Replace('-', '+').Replace('_', '/');

    if (base64.Length % 4 != 0)
    {
      base64 += new string('=', 4 - base64.Length % 4);
    }

    var payloadBytes = Convert.FromBase64String(base64);

    // Payload can be parsed as JSON, and then fields such expiration date, scope, etc can be accessed
    var payload = JsonDocument.Parse(payloadBytes);

    // For brevity we will simply output formatted json to console
    var formatted = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
      WriteIndented = true,
    });
    Console.WriteLine($"{name}: '{token}'");
    Console.WriteLine($"{formatted}");
    Console.WriteLine();
  }


}
