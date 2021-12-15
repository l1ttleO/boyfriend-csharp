﻿using System.Reflection;
using Boyfriend.Commands;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace Boyfriend;

public class EventHandler {
    private readonly DiscordSocketClient _client = Boyfriend.Client;
    public static readonly CommandService Commands = new();

    public async Task InitEvents() {
        _client.Ready += ReadyEvent;
        _client.MessageDeleted += MessageDeletedEvent;
        _client.MessageReceived += MessageReceivedEvent;
        _client.MessageUpdated += MessageUpdatedEvent;
        _client.UserJoined += UserJoinedEvent;
        await Commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);
    }

    private static async Task ReadyEvent() {
        await Boyfriend.SetupGuildConfigs();

        foreach (var guild in Boyfriend.Client.Guilds) {
            var channel = guild.GetTextChannel(Boyfriend.GetGuildConfig(guild).BotLogChannel);
            if (channel == null) continue;
            await channel.SendMessageAsync($"{Utils.GetBeep()}Я запустился! (C#)");
        }
    }

    private static async Task MessageDeletedEvent(Cacheable<IMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel) {
        var msg = message.Value;
        var toSend = msg == null
            ? $"Удалено сообщение в канале {Utils.MentionChannel(channel.Id)}, но я забыл что там было"
            : $"Удалено сообщение от {msg.Author.Mention} в канале " +
              $"{Utils.MentionChannel(channel.Id)}: {Environment.NewLine}{Utils.Wrap(msg.Content)}";
        try {
            await Utils.SilentSendAsync(await Utils.GetAdminLogChannel(
                    Boyfriend.FindGuild(channel.Value as ITextChannel)), toSend);
        } catch (ArgumentException) {}
    }

    private static async Task MessageReceivedEvent(SocketMessage messageParam) {
        if (messageParam is not SocketUserMessage message) return;
        var user = (IGuildUser) message.Author;
        var guild = user.Guild;
        var argPos = 0;

        if ((message.MentionedUsers.Count > 3 || message.MentionedRoles.Count > 2)
            && !user.GuildPermissions.MentionEveryone)
            BanModule.BanUser(guild, await guild.GetCurrentUserAsync(), user, TimeSpan.FromMilliseconds(-1),
                "Более 3-ёх упоминаний в одном сообщении");

        var prevs = await message.Channel.GetMessagesAsync(3).FlattenAsync();
        var prevsArray = prevs as IMessage[] ?? prevs.ToArray();
        var prev = prevsArray[1].Content;
        var prevFailsafe = prevsArray[2].Content;
        if (message.Channel is not ITextChannel channel) throw new Exception();
        if (!(message.HasStringPrefix(Boyfriend.GetGuildConfig(guild).Prefix, ref argPos)
              || message.HasMentionPrefix(Boyfriend.Client.CurrentUser, ref argPos))
            || user == await Boyfriend.FindGuild(channel).GetCurrentUserAsync()
            || user.IsBot && message.Content.Contains(prev) || message.Content.Contains(prevFailsafe))
            return;

        await CommandHandler.HandleCommand(message, argPos);
    }

    private static async Task MessageUpdatedEvent(Cacheable<IMessage, ulong> messageCached, SocketMessage messageSocket,
        ISocketMessageChannel channel) {
        var msg = messageCached.Value;
        var nl = Environment.NewLine;
        if (msg.Content == messageSocket.Content) return;
        var toSend = msg == null
            ? $"Отредактировано сообщение от {messageSocket.Author.Mention} в канале" +
              $" {Utils.MentionChannel(channel.Id)}," + " но я забыл что там было до редактирования: " +
              Utils.Wrap(messageSocket.Content)
            : $"Отредактировано сообщение от {msg.Author.Mention} " +
              $"в канале {Utils.MentionChannel(channel.Id)}." +
              $"{nl}До:{nl}{Utils.Wrap(msg.Content)}{nl}После:{nl}{Utils.Wrap(messageSocket.Content)}";
        try {
            await Utils.SilentSendAsync(await Utils.GetAdminLogChannel(Boyfriend.FindGuild(channel as ITextChannel)),
                toSend);
        } catch (ArgumentException) {}
    }

    private static async Task UserJoinedEvent(SocketGuildUser user) {
        var guild = user.Guild;
        await guild.SystemChannel.SendMessageAsync($"{user.Mention}, добро пожаловать на сервер {guild.Name}");
    }
}