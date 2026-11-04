namespace RouteGo.Search.Api.Tests.Support;

/// <summary>One logged line: whether it was an error, and its rendered text.</summary>
public sealed record LoggedEntry(bool IsError, string Message);
