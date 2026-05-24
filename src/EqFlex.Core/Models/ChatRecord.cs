namespace EqFlex.Core.Models;

public enum ChatChannel { Say, Tell, Shout, Auction, Guild, Fellowship, Group, Raid, OOC, Emote, Other }

public sealed record ChatRecord(
    string Speaker,
    string Message,
    ChatChannel Channel,
    long Timestamp);
