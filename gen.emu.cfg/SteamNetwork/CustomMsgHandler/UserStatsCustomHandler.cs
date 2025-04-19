using SteamKit2;
using SteamKit2.Internal; // this namespace stores the generated protobuf message structures
using System.Text;
using System.Text.Json.Nodes;
using common.utils;
using common.utils.Logging;


// https://github.com/SteamRE/SteamKit/blob/master/Samples/010_Extending
namespace gen.emu.cfg.SteamNetwork.CustomMsgHandler;

public class UserStatsCustomHandler : ClientMsgHandler
{
  // define our custom callback class
  // this will pass data back to the user of the handler
  public class UserStatsAndAchievementsSchemaMsg : CallbackMsg
  {
    public EResult Result { get; private set; }
    public CMsgClientGetUserStatsResponse Msg { get; private set; }

    // generally we don't want user code to instantiate callback objects,
    // but rather only let handlers create them
    internal UserStatsAndAchievementsSchemaMsg(IPacketMsg packetMsg)
    {
      // in order to get at the message contents, we need to wrap the packet message
      // in an object that gives us access to the message body
      var protoMsg = new ClientMsgProtobuf<CMsgClientGetUserStatsResponse>(packetMsg);

      // we have to set our job id as the sender id so that the async job manager knows
      // which AsyncJob instance to finalize
      // notice that the sender id becomes the TargetJobID in the incoming proto msg
      JobID = protoMsg.TargetJobID;

      // the raw body of the message often doesn't make use of useful types, so we need to
      // cast them to types that are prettier for the user to handle
      Result = (EResult)protoMsg.Body.eresult;
      Msg = protoMsg.Body;

    }
  }


  public static void RegisterHandler(SteamClient steamClient)
  {
    ArgumentNullException.ThrowIfNull(steamClient);
    steamClient.AddHandler(new UserStatsCustomHandler());
  }


  // handlers can also define functions which can send data to the steam servers
  public AsyncJob<UserStatsAndAchievementsSchemaMsg> GetUserStats(uint appid, ulong userid)
  {
    var request = new ClientMsgProtobuf<CMsgClientGetUserStats>(EMsg.ClientGetUserStats);

    // this id essentially turns a generic callback into a specific callresult (using valve terms)
    // 1. we give each flying msg a unique id
    // 2. the async job manager will save this id for later along with
    //    the async job instance which has a TaskCompletionSource
    //    dictionary<SourceJobID, AsyncJob>
    // 3. later we set the JobID of the callback (which we post via Client.PostCallback) to this the same id 
    // 4. that way the async job manager can read the JobID and correlate it to the async job instance
    // 5. finally the async job manager wiil set the internal TaskCompletionSource with the callback result
    request.SourceJobID = Client.GetNextJobID();

    request.Body.game_id = appid;
    request.Body.steam_id_for_user = userid;
    request.Body.schema_local_version = -1; // get the latest schema
    request.Body.crc_stats = 0; // optional

    Client.Send(request);
    return new AsyncJob<UserStatsAndAchievementsSchemaMsg>(Client, request.SourceJobID);

  }

  // https://partner.steamgames.com/doc/webapi/isteamuserstats#GetGlobalStatsForGame
  // https://partner.steamgames.com/doc/features/achievements#global_stats
  // https://steamapi.xpaw.me/#ISteamUserStats/GetGlobalStatsForGame
  public async Task<(string StatName, double GlobalTotalAggregate)[]> GetGlobalStatsForGameAsync(uint appid, IList<string> stats, CancellationToken ct = default)
  {
    if (appid == 0)
    {
      throw new InvalidOperationException($"Invalid appid");
    }

    if (stats.Count == 0)
    {
      return [];
    }

    // we can't optimize this to request more than 1 item at a time
    // appid 102600 has these stats: "Chaos Chamber_won", "Chaos Chamber_failed", "Chaos Chamber_restarted"
    // and the returned json looks like this:
    //
    // {
    //   "response":{
    //     "globalstats":{
    //       "Chaos":{ "total":"1844501" },
    //       "Chaos":{ "total":"61493" },
    //       "Chaos":{ "total":"96996 "}
    //     },
    //     "result":1
    //   }
    // }
    //
    // because all stats end up having the same name, we have to request them 1 by 1 and ignore the name entirely

    const int STATS_BATCH = 10;
    var results = await Utils.ParallelJobsAsync(stats, async (statName, jobIdx, _, ct) =>
    {
      var detailsBytes = await Utils.WebRequestAsync(
        url: $"https://api.steampowered.com/ISteamUserStats/GetGlobalStatsForGame/v1",
        urlParams: new JsonObject
        {
          ["appid"] = appid,
          ["count"] = 1,
          ["name[0]"] = statName,
        },
        method: Utils.WebMethod.Get,
        cancelToken: ct
      ).ConfigureAwait(false);
      if (detailsBytes is null || detailsBytes.Length == 0)
      {
        string err = $"Failed to get a web response for the global total aggregate of stat '{statName}'";
        Log.Instance.Write(Log.Kind.Debug, err);
        throw new InvalidOperationException(err);
      }

      var detailsStr = Encoding.UTF8.GetString(detailsBytes);
      var jnode = JsonObject.Parse(detailsStr);
      var reponseJobj = jnode?.AsObject().GetKeyIgnoreCase("response");
      bool isOk = reponseJobj.GetKeyIgnoreCase("result").ToBoolSafe();
      if (!isOk)
      {
        string errMsg = reponseJobj.GetKeyIgnoreCase("error").ToStringSafe();
        string err = $"'result' property in the global total aggregate of stat '{statName}' is bad, error={errMsg}";
        Log.Instance.Write(Log.Kind.Debug, err);
        throw new InvalidOperationException(err);
      }

      double globalTotalAggregate = reponseJobj
        .GetKeyIgnoreCase("globalstats").ToObjSafe()
        .FirstOrDefault().Value
        .GetKeyIgnoreCase("total").ToNumSafe()
        ;
      return (statName, globalTotalAggregate);
    }, STATS_BATCH, 3, ct);

    return results.Where(r => r != default).ToArray();
  }

  public override void HandleMsg(IPacketMsg packetMsg)
  {
    // this function is called when a message arrives from the Steam network
    // the SteamClient class will pass the message along to every registered ClientMsgHandler

    // the MsgType exposes the EMsg (type) of the message
    switch (packetMsg.MsgType)
    {
      // we want to handle these messages only
      case EMsg.ClientGetUserStatsResponse:
        Client.PostCallback(new UserStatsAndAchievementsSchemaMsg(packetMsg));
        break;
    }
  }

}
