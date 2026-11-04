namespace RouteGo.Search.Api.Infrastructure.Messaging;

public sealed class RedrivePolicyTests
{
    [Fact]
    public void ReadMaxReceiveCount_CountAsString_ReturnsCount()
    {
        int? count = RedrivePolicy.ReadMaxReceiveCount(
            """{"deadLetterTargetArn":"arn:aws:sqs:ap-south-1:1:search-dlq","maxReceiveCount":"5"}""");

        Assert.Equal(5, count);
    }

    [Fact]
    public void ReadMaxReceiveCount_CountAsNumber_ReturnsCount()
    {
        int? count = RedrivePolicy.ReadMaxReceiveCount(
            """{"deadLetterTargetArn":"arn:aws:sqs:ap-south-1:1:search-dlq","maxReceiveCount":5}""");

        Assert.Equal(5, count);
    }

    [Fact]
    public void ReadMaxReceiveCount_NoPolicy_ReturnsNull()
    {
        int? count = RedrivePolicy.ReadMaxReceiveCount(null);

        Assert.Null(count);
    }
}
