using Orders.TestSupport;

namespace Orders.IntegrationTests;

[CollectionDefinition(OrdersCollectionNames.Environment)]
public sealed class OrdersCollection : ICollectionFixture<OrdersFixture>;
