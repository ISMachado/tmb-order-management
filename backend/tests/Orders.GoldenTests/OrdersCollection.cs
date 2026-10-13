using Orders.TestSupport;

namespace Orders.GoldenTests;

[CollectionDefinition(OrdersCollectionNames.Environment)]
public sealed class OrdersCollection : ICollectionFixture<OrdersFixture>;
