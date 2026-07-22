using System.Collections.Generic;

namespace Fairoots.Networking
{
    /// <summary>
    /// Translations for the Boarding Pass Start-confirmation dialog
    /// (<see cref="ModPresenceDialog"/>, gated by
    /// <see cref="BoardingPassStartGatePatch"/>). Array order matches
    /// <c>LocalizedText.Language</c>'s declaration order (English, French,
    /// Italian, German, SpanishSpain, SpanishLatam, BRPortuguese, Russian,
    /// Ukrainian, SimplifiedChinese, TraditionalChinese, Japanese, Korean,
    /// Polish, Turkish) - <see cref="LocalizationHelper"/> falls back to
    /// English (index 0) for any entry a language doesn't cover.
    /// TraditionalChinese is left blank, matching peak-checkpoint-save's
    /// <c>MessagesLocalization</c> convention (the game's own
    /// <c>LocalizedText.LANGUAGE_COUNT</c> is 14, one less than the 15-value
    /// enum, so this mirrors that precedent rather than guessing a
    /// translation for a language slot the game itself doesn't actually use).
    /// "Fairoots" (the mod's name) is left untranslated everywhere, same as
    /// how peak-checkpoint-save leaves its own and other mod names alone. No
    /// em dashes anywhere, per the maintainer's request.
    /// </summary>
    internal enum ModPresenceMsgKey
    {
        DialogTitle,
        DialogBody,
        ConfirmButton,
        CancelButton,
    }

    internal static class ModPresenceLocalization
    {
        private static readonly Dictionary<ModPresenceMsgKey, string[]> Table = new Dictionary<ModPresenceMsgKey, string[]>
        {
            [ModPresenceMsgKey.DialogTitle] = new[]
            {
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "",
                "Fairoots",
                "Fairoots",
                "Fairoots",
                "Fairoots",
            },
            [ModPresenceMsgKey.DialogBody] = new[]
            {
                "Not everyone in this lobby has Fairoots installed. This will create issues in the Roots biome for those clients (check LogOutput.log for more details). Start anyway?",
                "Tous les membres de ce lobby n'ont pas Fairoots installé. Cela causera des problèmes dans le biome Roots pour ces clients (consultez LogOutput.log pour plus de détails). Démarrer quand même ?",
                "Non tutti i membri di questa lobby hanno Fairoots installato. Questo causerà problemi nel bioma Roots per quei client (controlla LogOutput.log per maggiori dettagli). Avviare comunque?",
                "Nicht alle Mitglieder dieser Lobby haben Fairoots installiert. Dies wird bei diesen Clients zu Problemen im Roots-Biom führen (siehe LogOutput.log für weitere Details). Trotzdem starten?",
                "No todos los miembros de este lobby tienen Fairoots instalado. Esto causará problemas en el bioma Roots para esos clientes (consulta LogOutput.log para más detalles). ¿Iniciar de todos modos?",
                "No todos los miembros de este lobby tienen Fairoots instalado. Esto causará problemas en el bioma Roots para esos clientes (consulta LogOutput.log para más detalles). ¿Iniciar de todos modos?",
                "Nem todos os membros deste lobby têm o Fairoots instalado. Isso causará problemas no bioma Roots para esses clientes (verifique o LogOutput.log para mais detalhes). Iniciar mesmo assim?",
                "Не у всех участников этого лобби установлен Fairoots. Это вызовет проблемы в биоме Roots у этих игроков (подробности смотрите в LogOutput.log). Всё равно начать?",
                "Не всі учасники цього лобі мають встановлений Fairoots. Це спричинить проблеми в біомі Roots для цих клієнтів (подробиці дивіться в LogOutput.log). Все одно почати?",
                "并非大厅中的所有玩家都安装了 Fairoots。这将导致这些客户端在 Roots 生态群系中出现问题（详情请查看 LogOutput.log）。仍要开始吗？",
                "",
                "このロビーの全員が Fairoots をインストールしているわけではありません。これにより、該当クライアントの Roots バイオームで問題が発生します（詳細は LogOutput.log を確認してください）。それでも開始しますか？",
                "이 로비의 모든 플레이어가 Fairoots를 설치한 것은 아닙니다. 이는 해당 클라이언트의 Roots 바이옴에서 문제를 일으킵니다 (자세한 내용은 LogOutput.log를 확인하세요). 그래도 시작하시겠습니까?",
                "Nie wszyscy w tym lobby mają zainstalowany Fairoots. Spowoduje to problemy w biomie Roots u tych klientów (sprawdź LogOutput.log, aby uzyskać więcej informacji). Rozpocząć mimo to?",
                "Bu lobideki herkeste Fairoots yüklü değil. Bu, o istemcilerde Roots biyomunda sorunlara yol açacaktır (daha fazla ayrıntı için LogOutput.log dosyasına bakın). Yine de başlansın mı?",
            },
            [ModPresenceMsgKey.ConfirmButton] = new[]
            {
                "Start Anyway",
                "Démarrer quand même",
                "Avvia comunque",
                "Trotzdem starten",
                "Iniciar de todos modos",
                "Iniciar de todos modos",
                "Iniciar mesmo assim",
                "Всё равно начать",
                "Все одно почати",
                "仍要开始",
                "",
                "それでも開始",
                "그래도 시작",
                "Rozpocznij mimo to",
                "Yine de başlat",
            },
            [ModPresenceMsgKey.CancelButton] = new[]
            {
                "Cancel",
                "Annuler",
                "Annulla",
                "Abbrechen",
                "Cancelar",
                "Cancelar",
                "Cancelar",
                "Отмена",
                "Скасувати",
                "取消",
                "",
                "キャンセル",
                "취소",
                "Anuluj",
                "İptal",
            },
        };

        /// <summary>Text for the current value of <c>LocalizedText.CURRENT_LANGUAGE</c>.</summary>
        public static string Get(ModPresenceMsgKey key) => LocalizationHelper.Resolve(Table[key]);
    }
}
