using System.Text.Json;
using TdLib;
using TdLib.Bindings;
using static TdLib.TdApi;
using Update = TdLib.TdApi.Update;

namespace SearchBotApplication
{
    public class UserBot
    {
        private readonly ManualResetEventSlim ReadyToAuthenticate;
        private readonly Config currentUserConfig;
        private bool authNeeded;
        private bool passwordNeeded;

        public TdClient Client { get; set; }

        public UserBot(string pathToJsonFileConfig)
        {
            currentUserConfig = GetConfig(pathToJsonFileConfig);
            ReadyToAuthenticate = new ManualResetEventSlim();
            Client = new();
        }

        private static Config GetConfig(string pathToJsonFileConfig)
        {
            string jsonString = System.IO.File.ReadAllText(pathToJsonFileConfig);
            try
            {
                return JsonSerializer.Deserialize<Config>(jsonString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("JSON config file not found or empty", ex);
            }
        }
        public async Task StartSessionAsync()
        {
            Client.Bindings.SetLogVerbosityLevel(TdLogLevel.Fatal);

            Client.UpdateReceived += async (_, update) => { await ProcessUpdatesAsync(update); };

            ReadyToAuthenticate.Wait();

            if (authNeeded)
            {
                await HandleAuthenticationAsync();
            }

            Console.WriteLine($"Session is started");
        }
        private async Task HandleAuthenticationAsync()
        {
            await Client.ExecuteAsync(new SetAuthenticationPhoneNumber
            {
                PhoneNumber = currentUserConfig.PhoneNumber
            });

            Console.Write("Insert the login code: ");
            var code = Console.ReadLine();

            await Client.ExecuteAsync(new CheckAuthenticationCode
            {
                Code = code
            });

            if (!passwordNeeded) { return; }

            Console.Write("Insert the password: ");
            var password = Console.ReadLine();

            await Client.ExecuteAsync(new CheckAuthenticationPassword
            {
                Password = password
            });
        }
        private async Task ProcessUpdatesAsync(Update update)
        {
            switch (update)
            {
                case Update.UpdateAuthorizationState { AuthorizationState: AuthorizationState.AuthorizationStateWaitTdlibParameters }:

                    var filesLocation = Path.Combine(AppContext.BaseDirectory, "db");
                    await Client.ExecuteAsync(new SetTdlibParameters
                    {
                        ApiId = currentUserConfig.ApiId,
                        ApiHash = currentUserConfig.ApiHash,
                        DeviceModel = "PC",
                        SystemLanguageCode = "en",
                        ApplicationVersion = currentUserConfig.ApplicationVersion,
                        DatabaseDirectory = filesLocation,
                        FilesDirectory = filesLocation,
                    });
                    break;

                case Update.UpdateAuthorizationState { AuthorizationState: AuthorizationState.AuthorizationStateWaitPhoneNumber }:

                case Update.UpdateAuthorizationState { AuthorizationState: AuthorizationState.AuthorizationStateWaitCode }:
                    authNeeded = true;
                    ReadyToAuthenticate.Set();
                    break;

                case Update.UpdateAuthorizationState { AuthorizationState: AuthorizationState.AuthorizationStateWaitPassword }:
                    authNeeded = true;
                    passwordNeeded = true;
                    ReadyToAuthenticate.Set();
                    break;

                case Update.UpdateUser:
                    ReadyToAuthenticate.Set();
                    break;

                case Update.UpdateConnectionState { State: ConnectionState.ConnectionStateReady }:
                    break;
            }
        }      
        public async Task<Chat> GetBotChannelAsync()
        {
            var chats = await Client.ExecuteAsync(new GetChats { Limit = 100 });

            foreach (var chatId in chats.ChatIds)
            {
                var chat = await Client.ExecuteAsync(new GetChat
                {
                    ChatId = chatId
                });

                if (chat.Type is ChatType.ChatTypeSupergroup or ChatType.ChatTypeBasicGroup or ChatType.ChatTypePrivate)
                {
                    return chat;
                }
            }

            return null;
        }
        public static List<string> ParseUriIntoUsernames(string urls)
        {
            List<string> usernames = [];    
            urls = urls.Replace(" ", "").Replace("\n", "").Replace("\t","");
            var indexes = urls.AllIndexesOf("http");
            for (var i = 0; i < indexes.ToArray().Length; i++)
            {
                string url;
                if (i + 1 >= indexes.Count)
                {
                    url = urls[indexes[i]..];
                }
                else
                {
                    var lengthUrl = indexes[i + 1] - indexes[i];
                    url = urls.Substring(indexes[i], lengthUrl);

                }
                var lastSlashIndex = url.LastIndexOf('/');

                usernames.Add(url[(lastSlashIndex + 1)..]);
            }

            return usernames;
        }
        public async Task SendMessageAsync(long userId, string message)
        {          
            InputMessageContent.InputMessageText messageText = new()
            {
                Text = new() { Text = message }
            };

            await Client.SendMessageAsync(chatId:userId, inputMessageContent: messageText);
        }
        public async Task<List<Message>> SearchPostsAsync(Chat channel, string query)
        {
            var foundedMessages = await Client.SearchChatMessagesAsync(
            chatId: channel.Id,
            query: query,
            limit: 99
            );

            return [.. foundedMessages.Messages];
        }
        public async Task<Chat> SearchChatAsync(string username) => await Client.SearchPublicChatAsync(username);
        public async Task ForwardMessages(List<Message> messages, long forChatId)
        {
            foreach (var message in messages)
            {
                if (!message.CanBeForwarded)
                {
                    continue;
                }
                try
                {
                    await Client.ForwardMessagesAsync
                    (
                        chatId: forChatId,
                        fromChatId: message.ChatId,
                        messageIds: [message.Id],
                        sendCopy: true
                    );
                }
                catch(Exception e) 
                {
                    throw new InvalidOperationException($"Message {message.Id} from {message.ImportInfo.SenderName} do not forwarded", e);
                }  
            }

            Console.WriteLine($"Messages forwarded successfully");
        }
    }
}