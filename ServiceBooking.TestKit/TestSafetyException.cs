namespace ServiceBooking.TestKit;

/// <summary>
/// Бросается, когда код тестовой инфраструктуры пытается сделать разрушительное действие
/// (обычно DROP DATABASE) над именем, которое не доказанно принадлежит одноразовой тестовой базе
/// текущего прогона. См. ARCHITECTURE_CYCLE8.md §69.3.
/// </summary>
public sealed class TestSafetyException : Exception
{
    public TestSafetyException(string message) : base(message)
    {
    }

    public TestSafetyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
