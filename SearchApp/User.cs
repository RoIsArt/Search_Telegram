using static TdLib.TdApi;

namespace SearchBotApplication
{
    public class User
    {
        public required long Id { get; set; }
        public required string UserName { get; set; }
        public required State CurrentState { get; set; }
        public List<string>? ChannelsUsernames { get; set; }
        public string? Query {  get; set; }
        public Chat? ChatForSearching { get; set; }
        public List<Message>? MessagesToForward { get; set; }


        public void ResetSearchData()
        {
            Query = string.Empty;
            ChannelsUsernames?.Clear();
            MessagesToForward?.Clear();
            ChatForSearching = null;
        }
    }
}
