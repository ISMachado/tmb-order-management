namespace Orders.TestSupport;

public static class Eventually
{
    /// <summary>Repete a verificacao ate ela devolver um valor aceito ou estourar o prazo (sem Thread.Sleep fixo).</summary>
    public static async Task<T> ReturnsAsync<T>(Func<Task<T>> probe, Func<T, bool> accept, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        T last = await probe();
        while (!accept(last))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condicao nao atingida no prazo. {because} Ultimo valor: {last}");
            }

            await Task.Delay(100);
            last = await probe();
        }

        return last;
    }
}
