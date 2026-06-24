namespace Comuki.Platform.Routing.Forwarding;

/// <summary>Исход одной попытки форварда на апстрим.</summary>
public enum SendOutcome
{
    /// <summary>Ответ успешно стримнут клиенту. Цикл завершён.</summary>
    Success,

    /// <summary>Квота ключа исчерпана. Клиенту ничего не записано — нужен retry на следующем ключе.</summary>
    Exhausted,

    /// <summary>Не-quota ошибка (или сетевой сбой) уже проброшена клиенту. Цикл завершён.</summary>
    PassedThroughError,
}
