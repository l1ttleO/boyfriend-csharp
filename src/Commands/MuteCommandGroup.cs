using System.ComponentModel;
using System.Text;
using Boyfriend.Data;
using Boyfriend.Services;
using JetBrains.Annotations;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Extensions.Embeds;
using Remora.Discord.Extensions.Formatting;
using Remora.Rest.Core;
using Remora.Results;

namespace Boyfriend.Commands;

/// <summary>
///     Handles commands related to mute management: /mute and /unmute.
/// </summary>
[UsedImplicitly]
public class MuteCommandGroup : CommandGroup {
    private readonly ICommandContext      _context;
    private readonly GuildDataService     _dataService;
    private readonly FeedbackService      _feedbackService;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestUserAPI  _userApi;
    private readonly UtilityService       _utility;

    public MuteCommandGroup(
        ICommandContext      context,  GuildDataService    dataService, FeedbackService feedbackService,
        IDiscordRestGuildAPI guildApi, IDiscordRestUserAPI userApi,     UtilityService  utility) {
        _context = context;
        _dataService = dataService;
        _feedbackService = feedbackService;
        _guildApi = guildApi;
        _userApi = userApi;
        _utility = utility;
    }

    /// <summary>
    ///     A slash command that mutes a Discord member with the specified reason.
    /// </summary>
    /// <param name="target">The member to mute.</param>
    /// <param name="duration">The duration for this mute. The member will be automatically unmuted after this duration.</param>
    /// <param name="reason">
    ///     The reason for this mute. Must be encoded with <see cref="Extensions.EncodeHeader" /> when passed to
    ///     <see cref="IDiscordRestGuildAPI.ModifyGuildMemberAsync" />.
    /// </param>
    /// <returns>
    ///     A feedback sending result which may or may not have succeeded. A successful result does not mean that the member
    ///     was muted and vice-versa.
    /// </returns>
    /// <seealso cref="ExecuteUnmute" />
    [Command("mute", "мут")]
    [DiscordDefaultMemberPermissions(DiscordPermission.ModerateMembers)]
    [DiscordDefaultDMPermission(false)]
    [RequireContext(ChannelContext.Guild)]
    [RequireDiscordPermission(DiscordPermission.ModerateMembers)]
    [RequireBotDiscordPermissions(DiscordPermission.ModerateMembers)]
    [Description("Mute member")]
    [UsedImplicitly]
    public async Task<Result> ExecuteMute(
        [Description("Member to mute")] IUser    target,
        [Description("Mute reason")]    string   reason,
        [Description("Mute duration")]  TimeSpan duration) {
        if (!_context.TryGetContextIDs(out var guildId, out var channelId, out var userId))
            return Result.FromError(
                new ArgumentNullError(nameof(_context), "Unable to retrieve necessary IDs from command context"));

        // The current user's avatar is used when sending error messages
        var currentUserResult = await _userApi.GetCurrentUserAsync(CancellationToken);
        if (!currentUserResult.IsDefined(out var currentUser))
            return Result.FromError(currentUserResult);

        var userResult = await _userApi.GetUserAsync(userId.Value, CancellationToken);
        if (!userResult.IsDefined(out var user))
            return Result.FromError(userResult);

        var data = await _dataService.GetData(guildId.Value, CancellationToken);
        Messages.Culture = GuildSettings.Language.Get(data.Settings);

        var memberResult = await _guildApi.GetGuildMemberAsync(guildId.Value, target.ID, CancellationToken);
        if (!memberResult.IsSuccess) {
            var embed = new EmbedBuilder().WithSmallTitle(Messages.UserNotFoundShort, currentUser)
                .WithColour(ColorsList.Red).Build();

            return await _feedbackService.SendContextualEmbedResultAsync(embed, CancellationToken);
        }

        return await MuteUserAsync(
            target, reason, duration, guildId.Value, data, channelId.Value, user, currentUser, CancellationToken);
    }

    private async Task<Result> MuteUserAsync(
        IUser target, string reason,      TimeSpan duration, Snowflake guildId, GuildData data, Snowflake channelId,
        IUser user,   IUser  currentUser, CancellationToken ct = default) {
        var interactionResult
            = await _utility.CheckInteractionsAsync(
                guildId, user.ID, target.ID, "Mute", ct);
        if (!interactionResult.IsSuccess)
            return Result.FromError(interactionResult);

        if (interactionResult.Entity is not null) {
            var failedEmbed = new EmbedBuilder().WithSmallTitle(interactionResult.Entity, currentUser)
                .WithColour(ColorsList.Red).Build();

            return await _feedbackService.SendContextualEmbedResultAsync(failedEmbed, ct);
        }

        var until = DateTimeOffset.UtcNow.Add(duration); // >:)
        var muteResult = await _guildApi.ModifyGuildMemberAsync(
            guildId, target.ID, reason: $"({user.GetTag()}) {reason}".EncodeHeader(),
            communicationDisabledUntil: until, ct: ct);
        if (!muteResult.IsSuccess)
            return Result.FromError(muteResult.Error);

        var title = string.Format(Messages.UserMuted, target.GetTag());
        var description = new StringBuilder().AppendLine(string.Format(Messages.DescriptionActionReason, reason))
            .Append(
                string.Format(
                    Messages.DescriptionActionExpiresAt, Markdown.Timestamp(until))).ToString();

        var logResult = _utility.LogActionAsync(
            data.Settings, channelId, user, title, description, target, ColorsList.Red, ct: ct);
        if (!logResult.IsSuccess)
            return Result.FromError(logResult.Error);

        var embed = new EmbedBuilder().WithSmallTitle(
                string.Format(Messages.UserMuted, target.GetTag()), target)
            .WithColour(ColorsList.Green).Build();

        return await _feedbackService.SendContextualEmbedResultAsync(embed, ct);
    }

    /// <summary>
    ///     A slash command that unmutes a Discord member with the specified reason.
    /// </summary>
    /// <param name="target">The member to unmute.</param>
    /// <param name="reason">
    ///     The reason for this unmute. Must be encoded with <see cref="Extensions.EncodeHeader" /> when passed to
    ///     <see cref="IDiscordRestGuildAPI.ModifyGuildMemberAsync" />.
    /// </param>
    /// <returns>
    ///     A feedback sending result which may or may not have succeeded. A successful result does not mean that the member
    ///     was unmuted and vice-versa.
    /// </returns>
    /// <seealso cref="ExecuteMute" />
    /// <seealso cref="GuildUpdateService.TickGuildAsync"/>
    [Command("unmute", "размут")]
    [DiscordDefaultMemberPermissions(DiscordPermission.ModerateMembers)]
    [DiscordDefaultDMPermission(false)]
    [RequireContext(ChannelContext.Guild)]
    [RequireDiscordPermission(DiscordPermission.ModerateMembers)]
    [RequireBotDiscordPermissions(DiscordPermission.ModerateMembers)]
    [Description("Unmute member")]
    [UsedImplicitly]
    public async Task<Result> ExecuteUnmute(
        [Description("Member to unmute")] IUser  target,
        [Description("Unmute reason")]    string reason) {
        if (!_context.TryGetContextIDs(out var guildId, out var channelId, out var userId))
            return Result.FromError(
                new ArgumentNullError(nameof(_context), "Unable to retrieve necessary IDs from command context"));

        // The current user's avatar is used when sending error messages
        var currentUserResult = await _userApi.GetCurrentUserAsync(CancellationToken);
        if (!currentUserResult.IsDefined(out var currentUser))
            return Result.FromError(currentUserResult);

        // Needed to get the tag and avatar
        var userResult = await _userApi.GetUserAsync(userId.Value, CancellationToken);
        if (!userResult.IsDefined(out var user))
            return Result.FromError(userResult);

        var data = await _dataService.GetData(guildId.Value, CancellationToken);
        Messages.Culture = GuildSettings.Language.Get(data.Settings);

        var memberResult = await _guildApi.GetGuildMemberAsync(guildId.Value, target.ID, CancellationToken);
        if (!memberResult.IsSuccess) {
            var embed = new EmbedBuilder().WithSmallTitle(Messages.UserNotFoundShort, currentUser)
                .WithColour(ColorsList.Red).Build();

            return await _feedbackService.SendContextualEmbedResultAsync(embed, CancellationToken);
        }

        return await UnmuteUserAsync(
            target, reason, guildId.Value, data, channelId.Value, user, currentUser, CancellationToken);
    }

    private async Task<Result> UnmuteUserAsync(
        IUser target,      string            reason, Snowflake guildId, GuildData data, Snowflake channelId, IUser user,
        IUser currentUser, CancellationToken ct = default) {
        var interactionResult
            = await _utility.CheckInteractionsAsync(
                guildId, user.ID, target.ID, "Unmute", ct);
        if (!interactionResult.IsSuccess)
            return Result.FromError(interactionResult);

        if (interactionResult.Entity is not null) {
            var failedEmbed = new EmbedBuilder().WithSmallTitle(interactionResult.Entity, currentUser)
                .WithColour(ColorsList.Red).Build();

            return await _feedbackService.SendContextualEmbedResultAsync(failedEmbed, ct);
        }

        var unmuteResult = await _guildApi.ModifyGuildMemberAsync(
            guildId, target.ID, $"({user.GetTag()}) {reason}".EncodeHeader(),
            communicationDisabledUntil: null, ct: ct);
        if (!unmuteResult.IsSuccess)
            return Result.FromError(unmuteResult.Error);

        var title = string.Format(Messages.UserUnmuted, target.GetTag());
        var description = string.Format(Messages.DescriptionActionReason, reason);
        var logResult = _utility.LogActionAsync(
            data.Settings, channelId, user, title, description, target, ColorsList.Green, ct: ct);
        if (!logResult.IsSuccess)
            return Result.FromError(logResult.Error);

        var embed = new EmbedBuilder().WithSmallTitle(
                string.Format(Messages.UserUnmuted, target.GetTag()), target)
            .WithColour(ColorsList.Green).Build();

        return await _feedbackService.SendContextualEmbedResultAsync(embed, ct);
    }
}
