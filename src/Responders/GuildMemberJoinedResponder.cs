using Boyfriend.Data;
using Boyfriend.Services;
using JetBrains.Annotations;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Extensions.Embeds;
using Remora.Discord.Gateway.Responders;
using Remora.Results;

namespace Boyfriend.Responders;

/// <summary>
///     Handles sending a guild's <see cref="GuildSettings.WelcomeMessage" /> if one is set.
///     If <see cref="GuildSettings.ReturnRolesOnRejoin" /> is enabled, roles will be returned.
/// </summary>
/// <seealso cref="GuildSettings.WelcomeMessage" />
[UsedImplicitly]
public class GuildMemberJoinedResponder : IResponder<IGuildMemberAdd>
{
    private readonly IDiscordRestChannelAPI _channelApi;
    private readonly IDiscordRestGuildAPI _guildApi;
    private readonly GuildDataService _guildData;

    public GuildMemberJoinedResponder(
        IDiscordRestChannelAPI channelApi, GuildDataService guildData, IDiscordRestGuildAPI guildApi)
    {
        _channelApi = channelApi;
        _guildData = guildData;
        _guildApi = guildApi;
    }

    public async Task<Result> RespondAsync(IGuildMemberAdd gatewayEvent, CancellationToken ct = default)
    {
        if (!gatewayEvent.User.IsDefined(out var user))
        {
            return new ArgumentNullError(nameof(gatewayEvent.User));
        }

        var data = await _guildData.GetData(gatewayEvent.GuildID, ct);
        var cfg = data.Settings;
        var memberData = data.GetOrCreateMemberData(user.ID);

        if (GuildSettings.ReturnRolesOnRejoin.Get(cfg))
        {
            var result = await _guildApi.ModifyGuildMemberAsync(
                gatewayEvent.GuildID, user.ID,
                roles: memberData.Roles.ConvertAll(r => r.ToSnowflake()), ct: ct);
            if (!result.IsSuccess)
            {
                return Result.FromError(result.Error);
            }
        }

        if (GuildSettings.PublicFeedbackChannel.Get(cfg).Empty()
            || GuildSettings.WelcomeMessage.Get(cfg) is "off" or "disable" or "disabled")
        {
            return Result.FromSuccess();
        }

        Messages.Culture = GuildSettings.Language.Get(cfg);
        var welcomeMessage = GuildSettings.WelcomeMessage.Get(cfg) is "default" or "reset"
            ? Messages.DefaultWelcomeMessage
            : GuildSettings.WelcomeMessage.Get(cfg);

        var guildResult = await _guildApi.GetGuildAsync(gatewayEvent.GuildID, ct: ct);
        if (!guildResult.IsDefined(out var guild))
        {
            return Result.FromError(guildResult);
        }

        var embed = new EmbedBuilder()
            .WithSmallTitle(string.Format(welcomeMessage, user.GetTag(), guild.Name), user)
            .WithGuildFooter(guild)
            .WithTimestamp(gatewayEvent.JoinedAt)
            .WithColour(ColorsList.Green)
            .Build();
        if (!embed.IsDefined(out var built))
        {
            return Result.FromError(embed);
        }

        return (Result)await _channelApi.CreateMessageAsync(
            GuildSettings.PublicFeedbackChannel.Get(cfg), embeds: new[] { built },
            allowedMentions: Boyfriend.NoMentions, ct: ct);
    }
}
