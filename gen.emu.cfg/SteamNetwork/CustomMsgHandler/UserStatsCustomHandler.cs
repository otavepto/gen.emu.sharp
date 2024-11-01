using SteamKit2;
using SteamKit2.Internal; // this namespace stores the generated protobuf message structures


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

  public override void HandleMsg(IPacketMsg packetMsg)
  {
    // this function is called when a message arrives from the Steam network
    // the SteamClient class will pass the message along to every registered ClientMsgHandler

    // the MsgType exposes the EMsg (type) of the message
    switch (packetMsg.MsgType)
    {
      // we want to handle this message only
      case EMsg.ClientGetUserStatsResponse:
        HandleResponse(packetMsg);
        break;
    }
  }

  void HandleResponse(IPacketMsg packetMsg)
  {
    Client.PostCallback(new UserStatsAndAchievementsSchemaMsg(packetMsg));
  }

}
