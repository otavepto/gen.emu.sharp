using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Discovery;
using common.utils;

namespace gen.emu.cfg.SteamNetwork;

public class Client
{
  private static Client? _instance;
  public static Client Instance => _instance ??= new Client();

  const string CELLID_FILENAME = "cellid";
  const string SERVERS_LIST_FILENAME = "servers_list";

  const string SERVERS_FOLDER_NAME = @"servers";

  string servers_base_dir = SERVERS_FOLDER_NAME;
  string cellIdFilepath = CELLID_FILENAME;

  SteamClient steamClient;
  CallbackManager callbackManager;
  uint cellid;
  bool loaded;

  public SteamClient GetSteamClient => loaded ? steamClient : throw new InvalidOperationException("Not loaded yet");
  public CallbackManager GetCallbackManager => loaded ? callbackManager : throw new InvalidOperationException("Not loaded yet");
  public uint GetCellId => cellid;


  public void Init(string baseFolder)
  {
    if (loaded)
    {
      return;
    }

    loaded = true;

    servers_base_dir = Path.Combine(baseFolder, SERVERS_FOLDER_NAME);
    cellIdFilepath = Path.Combine(servers_base_dir, CELLID_FILENAME);

    TryReadLastCellId();
    var serversListFilepath = Path.Combine(servers_base_dir, SERVERS_LIST_FILENAME);
    var configuration = SteamConfiguration.Create(b =>
      b.WithCellID(cellid)
       .WithServerListProvider(new FileStorageServerListProvider(serversListFilepath))
       .WithConnectionTimeout(TimeSpan.FromMinutes(5))
    );

    // create our steamclient instance
    steamClient = new SteamClient(configuration);
    // create the callback manager which will route callbacks to function calls
    callbackManager = new CallbackManager(steamClient);
  }

  public void TryWriteLastCellId(uint newCellId)
  {
    try
    {
      cellid = newCellId;
      Directory.CreateDirectory(servers_base_dir);
      File.WriteAllText(cellIdFilepath, newCellId.ToString(CultureInfo.InvariantCulture), Utils.Utf8EncodingNoBom);
    }
    catch
    {

    }
  }

  void TryReadLastCellId()
  {
    try
    {
      // if we've previously connected and saved our cellid, load it.
      if (File.Exists(cellIdFilepath))
      {
        if (uint.TryParse(File.ReadAllText(cellIdFilepath), CultureInfo.InvariantCulture, out var newCellId))
        {
          cellid = newCellId;
        }
      }
    }
    catch
    {

    }
  }

}
