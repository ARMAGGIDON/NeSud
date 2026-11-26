namespace NeSud.Core;

public sealed record AccountSettings(string Username, string? Email, bool Verified, string? Invite, int Referrals, bool HasAvatar);
public sealed record ProfileEdit(string Username, string? Email);
public sealed record VerificationRequest(string Code);
public sealed record AvatarEdit(byte[]? Png);
