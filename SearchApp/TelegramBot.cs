using TdLib;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using static TdLib.TdApi;
using Update = Telegram.Bot.Types.Update;

namespace SearchBotApplication
{
    public class SearchBot
    {
        private static readonly string token = Environment.GetEnvironmentVariable("SEARCH_BOT_TOKEN");
        private static readonly string pathToJsonConfig = "DataConfig.json";
        private static Dictionary<long, User> Users { get; set; }
        private static Dictionary<string, Func<long, string>> Commands;
        public TelegramBotClient BotClient { get; set; }
        public UserBot UserBotClient { get; set; }

        public SearchBot(TelegramBotClient botClient, UserBot userBot)
        {
            BotClient = botClient;
            UserBotClient = userBot;
            Users = [];
        }

        public static async Task Main()
        {
            var bot = await InitializeBotAsync(token);
            Console.WriteLine($"Initialize SeacrhBot Client successfull");
            bot.InitializeCommands();

            using CancellationTokenSource cts = new();

            var me = await bot.BotClient.GetMeAsync();
            var botChannel = await bot.UserBotClient.GetBotChannelAsync();

            bot.BotClient.StartReceiving(
                updateHandler: bot.HandleUpdateAsync,
                pollingErrorHandler: bot.HandlePollingErrorAsync,
                receiverOptions: new ReceiverOptions() { AllowedUpdates = [] },
                cancellationToken: cts.Token);

            Console.WriteLine($"SeacrhBot start receiving\n" +
                              $"[{botChannel.Title}]: Id -> [{botChannel.Id}]\n" +
                              $"Start listening for @{me.Username}");
            Console.ReadLine();

            cts.Cancel();
        }
        private static async Task<SearchBot> InitializeBotAsync(string token)
        {
            TelegramBotClient botClient = new(token);
            Console.WriteLine($"Initialize TelegramBot Client successfull");

            UserBot userBot = new(pathToJsonConfig);
            Console.WriteLine($"Initialize UserBot Client successfull");
            await userBot.StartSessionAsync();
            return new SearchBot(botClient, userBot);
        }
        private void InitializeCommands()
        {
            Commands = new Dictionary<string, Func<long, string>>()
            {
                {"/set_channels", EntrySetChannelsState},
                {"/set_query", EntryQueryState},
                { "/start_searching", EntrySearchState},
                { "/reset_channel", EntryResetChannelsState}
            };

            Console.WriteLine($"Initialize commands successfull");
        }
        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message)
            {
                return;
            }
            if (message.Text is not { } messageText)
            {
                return;
            }

            var userId = message.From.Id;  
            
            if(!Users.ContainsKey(userId))
            {
                var chat = await UserBotClient.SearchChatAsync(message.From.Username);
                if(chat != null) 
                {
                    AddUser(userId, message.From.Username);
                    await UserBotClient.SendMessageAsync(Users[userId].Id, "Привет! Я провожу поиск каналов! Я буду присылать тебе найденные посты!");
                }
                
            }

            if(Commands.TryGetValue(messageText, out Func<long, string>? action))
            {
                var enterMessage = action.Invoke(userId);

                if (Users[userId].CurrentState != State.SEARCHING)
                {
                    await SendTextMessageAsync(enterMessage, userId);
                    return;
                }
                if (Users[userId].ChannelsUsernames.Count <= 0)
                {
                    await SendTextMessageAsync("Не заданы каналы для поиска!", userId);
                    return;
                }
                if (Users[userId].Query == null || Users[userId].Query == string.Empty)
                {
                    await SendTextMessageAsync("Не задана фраза для поиска!", userId);
                    return;
                }
            }

            switch (Users[userId].CurrentState)
            {
                case State.SET_CHANNELS:

                    Uri? outUri;
                    if (Uri.TryCreate(messageText, UriKind.Absolute, out outUri)
                            && (outUri.Scheme == Uri.UriSchemeHttp || outUri.Scheme == Uri.UriSchemeHttps))
                    {
                        Users[userId].ChannelsUsernames = UserBot.ParseUriIntoUsernames(messageText);
                        Console.WriteLine($"User {userId} добавил в список каналов новые каналы");
                        await SendTextMessageAsync("Ссылка принята!", userId);
                    }
                    else
                    {
                        await SendTextMessageAsync("Введите коректный адрес канала. Пример адреса: https://t.me/username", userId);
                    }

                    break;

                case State.SET_QUERY:

                    Users[userId].Query = messageText;
                    await SendTextMessageAsync("Ключевая фраза задана!", userId);
                    SetUserState(State.DEFAULT, userId);
                    break;

                case State.SEARCHING:

                    foreach (var username in Users[userId].ChannelsUsernames)
                    {
                        Users[userId].ChatForSearching = await UserBotClient.SearchChatAsync(username);
                        if (Users[userId].ChatForSearching == null)
                        {
                            Console.WriteLine($"Channel with username {username} not found");
                            continue;
                        }

                        Users[userId].MessagesToForward = await UserBotClient.SearchPostsAsync(Users[userId].ChatForSearching, Users[userId].Query);
                        if (Users[userId].MessagesToForward.Count <= 0)
                        {
                            Console.WriteLine($"In channel {username} messages not found");
                            continue;
                        }

                        Console.WriteLine($"In channel {username} founded {Users[userId].MessagesToForward.Count} messages\n");
                        await UserBotClient.SendMessageAsync(
                            Users[userId].Id,
                            $"В канале \"{username}\" найденно {Users[userId].MessagesToForward.Count} сообщений по запросу \"{Users[userId].Query}\"");
                        await UserBotClient.ForwardMessages(Users[userId].MessagesToForward, Users[userId].Id);

                        Users[userId].MessagesToForward?.Clear();
                        Users[userId].ChatForSearching = null;
                    }

                    Console.WriteLine($"Searching is completed");
                    Users[userId].ResetSearchData();
                    SetUserState(State.DEFAULT, userId);
                    break;

                case State.DEFAULT:
                    break;
            }
        }
        private static void SetUserState(State currentState, long chatId)
        {
            Users[chatId].CurrentState = currentState;
        }
        private static void AddUser(long id, string username)
        {
            Users.Add(
                    key: id,
                    value: new User()
                    {
                        Id = id,
                        UserName = username,
                        CurrentState = State.DEFAULT,
                        ChannelsUsernames = [],
                        Query = string.Empty,
                        MessagesToForward = [],
                        ChatForSearching = null,
                    });

            Console.WriteLine($"User: {id}, Username: {username} -> добавлен в список пользователей");
        }  
        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
        private async Task SendTextMessageAsync(string text, long forChatd)
        {
            await BotClient.SendTextMessageAsync(chatId: forChatd, text: text);
        }
        private string EntrySetChannelsState(long chatId)
        {
            SetUserState(State.SET_CHANNELS, chatId);
            return "Введите ссылки на каналы для поиска!";
        }
        private string EntryQueryState(long chatId)
        {
            SetUserState(State.SET_QUERY, chatId);
            return "Введите ключевую фразу для поиска!";
        }
        private string EntrySearchState(long chatId)
        {
            SetUserState(State.SEARCHING, chatId);
            return "Поиск начался!";
        }
        private string EntryResetChannelsState(long chatId)
        {
            Users[chatId].ChannelsUsernames?.Clear();
            SetUserState(State.DEFAULT, chatId);
            return "Список каналов сброшен!";
        }
    }
}
