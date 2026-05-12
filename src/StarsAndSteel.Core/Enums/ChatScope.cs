namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Visibility scope for a <see cref="Entities.ChatMessage"/>.
/// <list type="bullet">
///   <item><see cref="Global"/> — every player in the world sees it. <c>ToPlayerId</c> is null.</item>
///   <item><see cref="Alliance"/> — only players currently in <see cref="DiplomaticStatus.Allied"/>
///         with the sender (plus the sender themselves) see it. <c>ToPlayerId</c> is null;
///         the recipient set is computed at read time from <c>DiplomaticRelation</c>.</item>
///   <item><see cref="Direct"/> — only the sender and <c>ToPlayerId</c> see it.</item>
/// </list>
/// </summary>
public enum ChatScope
{
    Global = 0,
    Alliance = 1,
    Direct = 2
}
