using SteamKit2;
using SteamKit2.Internal; // this namespace stores the generated protobuf message structures


// https://github.com/SteamRE/SteamKit/blob/master/Samples/010_Extending
namespace gen.emu.cfg.SteamNetwork.CustomMsgHandler;

// Resources/Protobufs/artifact/steammessages_publishedfile.steamworkssdk.proto
class PublishedFileCustomHandler
{
  private static PublishedFileCustomHandler? _instance;
  public static PublishedFileCustomHandler Instance => _instance ??= new PublishedFileCustomHandler();


  public async Task<IReadOnlyList<PublishedFileDetails>> GetDetailsUgcAsync(IEnumerable<ulong> publishedFilesIds)
  {
    ArgumentNullException.ThrowIfNull(publishedFilesIds);
    if (!publishedFilesIds.Any())
    {
      return [];
    }

    var unifiedMessages = Client.Instance.GetSteamClient.GetHandler<SteamUnifiedMessages>();
    if (unifiedMessages is null)
    {
      throw new InvalidOperationException($"Null unified messages instance");
    }

    var requestMsg = new CPublishedFile_GetDetails_Request();
    requestMsg.publishedfileids.AddRange(publishedFilesIds);

    var responseMsg = await unifiedMessages.CreateService<PublishedFile>().GetDetails(requestMsg).ToTask().ConfigureAwait(false);
    if (responseMsg is null || responseMsg.Result != EResult.OK)
    {
      throw new InvalidDataException($"Bad response for published files details: {responseMsg?.Result}");
    }

    var result = responseMsg.Body;
    if (result is null || result.publishedfiledetails is null)
    {
      throw new InvalidOperationException($"Failed to deserialize published files details");
    }

    return result.publishedfiledetails;
  }

}
