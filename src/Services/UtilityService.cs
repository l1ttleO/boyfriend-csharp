using System.Drawing;
using System.Text;
using System.Text.Json.Nodes;
using Boyfriend.Data;
using Microsoft.Extensions.Hosting;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Extensions.Embeds;
using Remora.Discord.Extensions.Formatting;
using Remora.Rest.Core;
using Remora.Results;

namespace Boyfriend.Services;

/// <summary>
///     Provides utility methods that cannot be transformed to extension methods because they require usage
///     of some Discord APIs.
/// </summary>
public sealed class UtilityService : IHostedService
{
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestGuildScheduledEventAPI _eventApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly IDiscordRestUserAPI _userApi;

    public UtilityService(
        IDiscordRestChannelAPI channelApi, IDiscordRestGuildScheduledEventAPI eventApi, IDiscordRestGuildAPI guildApi,
        IDiscordRestUserAPI userApi)
    {
        _channelApi = channelApi;
        _eventApi = eventApi;
        _guildApi = guildApi;
        _userApi = userApi;
    }

    public Task StartAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Checks whether or not a member can interact with another member
    /// </summary>
    /// <param name="guildId">The ID of the guild in which an operation is being performed.</param>
    /// <param name="interacterId">The executor of the operation.</param>
    /// <param name="targetId">The target of the operation.</param>
    /// <param name="action">The operation.</param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>
    ///     <list type="bullet">
    ///         <item>A result which has succeeded with a null string if the member can interact with the target.</item>
    ///         <item>
    ///             A result which has succeeded with a non-null string containing the error message if the member cannot
    ///             interact with the target.
    ///         </item>
    ///         <item>A result which has failed if an error occurred during the execution of this method.</item>
    ///     </list>
    /// </returns>
    public async Task<Result<string?>> CheckInteractionsAsync(
        Snowflake guildId, Snowflake interacterId, Snowflake targetId, string action, CancellationToken ct = default)
    {
        if (interacterId == targetId)
        {
            return Result<string?>.FromSuccess($"UserCannot{action}Themselves".Localized());
        }

        var currentUserResult = await _userApi.GetCurrentUserAsync(ct);
        if (!currentUserResult.IsDefined(out var currentUser))
        {
            return Result<string?>.FromError(currentUserResult);
        }

        var guildResult = await _guildApi.GetGuildAsync(guildId, ct: ct);
        if (!guildResult.IsDefined(out var guild))
        {
            return Result<string?>.FromError(guildResult);
        }

        var targetMemberResult = await _guildApi.GetGuildMemberAsync(guildId, targetId, ct);
        if (!targetMemberResult.IsDefined(out var targetMember))
        {
            return Result<string?>.FromSuccess(null);
        }

        var currentMemberResult = await _guildApi.GetGuildMemberAsync(guildId, currentUser.ID, ct);
        if (!currentMemberResult.IsDefined(out var currentMember))
        {
            return Result<string?>.FromError(currentMemberResult);
        }

        var rolesResult = await _guildApi.GetGuildRolesAsync(guildId, ct);
        if (!rolesResult.IsDefined(out var roles))
        {
            return Result<string?>.FromError(rolesResult);
        }

        var interacterResult = await _guildApi.GetGuildMemberAsync(guildId, interacterId, ct);
        return interacterResult.IsDefined(out var interacter)
            ? CheckInteractions(action, guild, roles, targetMember, currentMember, interacter)
            : Result<string?>.FromError(interacterResult);
    }

    private static Result<string?> CheckInteractions(
        string action, IGuild guild, IReadOnlyList<IRole> roles, IGuildMember targetMember, IGuildMember currentMember,
        IGuildMember interacter)
    {
        if (!targetMember.User.IsDefined(out var targetUser))
        {
            return new ArgumentNullError(nameof(targetMember.User));
        }

        if (!interacter.User.IsDefined(out var interacterUser))
        {
            return new ArgumentNullError(nameof(interacter.User));
        }

        if (currentMember.User == targetMember.User)
        {
            return Result<string?>.FromSuccess($"UserCannot{action}Bot".Localized());
        }

        if (targetUser.ID == guild.OwnerID)
        {
            return Result<string?>.FromSuccess($"UserCannot{action}Owner".Localized());
        }

        var targetRoles = roles.Where(r => targetMember.Roles.Contains(r.ID)).ToList();
        var botRoles = roles.Where(r => currentMember.Roles.Contains(r.ID));

        var targetBotRoleDiff = targetRoles.MaxOrDefault(r => r.Position) - botRoles.MaxOrDefault(r => r.Position);
        if (targetBotRoleDiff >= 0)
        {
            return Result<string?>.FromSuccess($"BotCannot{action}Target".Localized());
        }

        if (interacterUser.ID == guild.OwnerID)
        {
            return Result<string?>.FromSuccess(null);
        }

        var interacterRoles = roles.Where(r => interacter.Roles.Contains(r.ID));
        var targetInteracterRoleDiff
            = targetRoles.MaxOrDefault(r => r.Position) - interacterRoles.MaxOrDefault(r => r.Position);
        return targetInteracterRoleDiff < 0
            ? Result<string?>.FromSuccess(null)
            : Result<string?>.FromSuccess($"UserCannot{action}Target".Localized());
    }

    /// <summary>
    ///     Gets the string mentioning the <see cref="GuildSettings.EventNotificationRole" /> and event subscribers related to
    ///     a scheduled
    ///     event.
    /// </summary>
    /// <param name="scheduledEvent">
    ///     The scheduled event whose subscribers will be mentioned.
    /// </param>
    /// <param name="settings">The settings of the guild containing the scheduled event</param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A result containing the string which may or may not have succeeded.</returns>
    public async Task<Result<string>> GetEventNotificationMentions(
        IGuildScheduledEvent scheduledEvent, JsonNode settings, CancellationToken ct = default)
    {
        var builder = new StringBuilder();
        var role = GuildSettings.EventNotificationRole.Get(settings);
        var usersResult = await _eventApi.GetGuildScheduledEventUsersAsync(
            scheduledEvent.GuildID, scheduledEvent.ID, withMember: true, ct: ct);
        if (!usersResult.IsDefined(out var users))
        {
            return Result<string>.FromError(usersResult);
        }

        if (role.Value is not 0)
        {
            builder.Append($"{Mention.Role(role)} ");
        }

        builder = users.Where(
                user =>
                {
                    if (!user.GuildMember.IsDefined(out var member))
                    {
                        return true;
                    }

                    return !member.Roles.Contains(role);
                })
            .Aggregate(builder, (current, user) => current.Append($"{Mention.User(user.User)} "));
        return builder.ToString();
    }

    /// <summary>
    ///     Logs an action in the <see cref="GuildSettings.PublicFeedbackChannel" /> and
    ///     <see cref="GuildSettings.PrivateFeedbackChannel" />.
    /// </summary>
    /// <param name="cfg">The guild configuration.</param>
    /// <param name="channelId">The ID of the channel where the action was executed.</param>
    /// <param name="user">The user who performed the action.</param>
    /// <param name="title">The title for the embed.</param>
    /// <param name="description">The description of the embed.</param>
    /// <param name="avatar">The user whose avatar will be displayed next to the <paramref name="title" /> of the embed.</param>
    /// <param name="color">The color of the embed.</param>
    /// <param name="isPublic">
    ///     Whether or not the embed should be sent in <see cref="GuildSettings.PublicFeedbackChannel" />
    /// </param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A result which has succeeded.</returns>
    public Result LogActionAsync(
        JsonNode cfg, Snowflake channelId, IUser user, string title, string description, IUser avatar,
        Color color, bool isPublic = true, CancellationToken ct = default)
    {
        var publicChannel = GuildSettings.PublicFeedbackChannel.Get(cfg);
        var privateChannel = GuildSettings.PrivateFeedbackChannel.Get(cfg);
        if (GuildSettings.PublicFeedbackChannel.Get(cfg).EmptyOrEqualTo(channelId)
            && GuildSettings.PrivateFeedbackChannel.Get(cfg).EmptyOrEqualTo(channelId))
        {
            return Result.FromSuccess();
        }

        var logEmbed = new EmbedBuilder().WithSmallTitle(title, avatar)
            .WithDescription(description)
            .WithActionFooter(user)
            .WithCurrentTimestamp()
            .WithColour(color)
            .Build();

        if (!logEmbed.IsDefined(out var logBuilt))
        {
            return Result.FromError(logEmbed);
        }

        var builtArray = new[] { logBuilt };

        // Not awaiting to reduce response time
        if (isPublic && publicChannel != channelId)
        {
            _ = _channelApi.CreateMessageAsync(
                publicChannel, embeds: builtArray,
                ct: ct);
        }

        if (privateChannel != publicChannel
            && privateChannel != channelId)
        {
            _ = _channelApi.CreateMessageAsync(
                privateChannel, embeds: builtArray,
                ct: ct);
        }

        return Result.FromSuccess();
    }
}