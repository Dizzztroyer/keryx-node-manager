namespace KeryxNodeManager.Core.Localization;

/// <summary>
/// Resource-lookup abstraction for Core-layer exception/status messages (PROJECT_STATUS.md "In
/// progress" item 1). Deliberately a plain static dictionary lookup, not .resx/ResourceManager -
/// this project already has a working, simple pattern for the App layer (XAML resource
/// dictionaries, see Resources/Strings.*.xaml), and .resx code-generation needs either a full
/// Visual Studio designer pass or fragile hand-maintained MSBuild generator wiring that this
/// project's build (plain `dotnet build`, no VS) shouldn't depend on. A plain
/// Dictionary&lt;language, Dictionary&lt;key, text&gt;&gt; has none of that risk, is trivially
/// unit-testable, and keeps <c>KeryxNodeManager.Core</c> free of any WPF/resource-assembly
/// dependency so it still builds/tests on Linux CI (see this project's csproj comment on why
/// Core is deliberately not net8.0-windows).
///
/// <see cref="Language"/> is set once by <c>App.xaml.cs</c>/<c>LocalizationManager</c> at startup
/// and again on every language switch - Core has no UI thread of its own to raise a
/// "language changed" event from, so callers are expected to read <see cref="Get"/> fresh each
/// time a message is actually produced (which every call site in this project already does -
/// these are all one-shot exception/status messages built at the moment something happens, never
/// cached across a language switch).
/// </summary>
public static class CoreStrings
{
    /// <summary>BCP-47-ish short code matching the App layer's LocalizationManager language keys
    /// (ru/en/es/it/fr/uk/de). Defaults to "ru" - this project's default/neutral language throughout,
    /// including the App layer's own Strings.ru.xaml being the first and most complete
    /// dictionary.</summary>
    public static string Language { get; set; } = "ru";

    /// <summary>
    /// Looks up <paramref name="key"/> in the current <see cref="Language"/>'s dictionary, falling
    /// back to Russian if the current language is missing that specific key (e.g. a translation
    /// gap), and finally falling back to the bare key itself if even Russian somehow doesn't have
    /// it - this must never throw, since it's called from exception-message construction and a
    /// missing-translation bug should degrade to an ugly-but-readable key, not crash the very code
    /// path that's already reporting an error.
    /// </summary>
    public static string Get(string key)
    {
        if (Resources.TryGetValue(Language, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (Resources.TryGetValue("ru", out var ruDict) && ruDict.TryGetValue(key, out var ruValue))
            return ruValue;
        return key;
    }

    /// <summary>Convenience wrapper for the common case of a lookup followed immediately by
    /// <see cref="string.Format(string, object?[])"/> - every interpolated Core-layer message in
    /// this project takes this shape (see PROJECT_STATUS.md for the full inventory this class
    /// replaces).</summary>
    public static string Format(string key, params object?[] args) => string.Format(Get(key), args);

    private static readonly Dictionary<string, Dictionary<string, string>> Resources = new()
    {
        ["ru"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " Планировщик заданий отклонил запрос на создание задачи. Обычно это не требует " +
                "прав администратора - если ошибка повторяется, попробуйте запустить это " +
                "приложение от имени администратора (правый клик → «Запуск от имени " +
                "администратора»); если и это не помогает, проверьте антивирус/защитное ПО.",
            ["TaskScheduler.RegisterFailed"] = "Не удалось создать задачу автозапуска (код {0}): {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "Не удалось удалить задачу автозапуска (код {0}): {1}",

            ["Profile.NotFound"] = "Профиль «{0}» не найден.",
            ["Profile.NameEmpty"] = "Имя профиля не может быть пустым.",
            ["Profile.AlreadyExists"] = "Профиль «{0}» уже существует.",
            ["Profile.CannotDeleteLast"] = "Нельзя удалить последний оставшийся профиль.",

            ["SystemChecker.WindowsVersionName"] = "Версия Windows",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0} — поддерживается.",
            ["SystemChecker.WindowsVersionTooOld"] = "Обнаружена {0}. Требуется Windows 10 или новее.",
            ["SystemChecker.GpuName"] = "Видеокарта NVIDIA",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi отработал, но не сообщил ни одной видеокарты.",
            ["SystemChecker.GpuFound"] = "Найдено: {0}",
            ["SystemChecker.WslName"] = "WSL (необязательно)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe не запустился.",
            ["SystemChecker.WslTimeout"] =
                "Проверка WSL не завершилась вовремя — не влияет на работу приложения (используется нативный режим).",
            ["SystemChecker.WslDetected"] = "WSL обнаружен и доступен.",
            ["SystemChecker.WslNotDetected"] =
                "WSL не обнаружен или не настроен — не требуется: приложение по умолчанию использует нативный режим Windows.",
            ["SystemChecker.WslNotFound"] =
                "wsl.exe не найден в системе — не требуется: приложение по умолчанию использует нативный режим Windows.",
            ["SystemChecker.DockerName"] = "Docker (необязательно)",
            ["SystemChecker.DockerFound"] = "docker.exe найден в PATH.",
            ["SystemChecker.DockerNotFound"] = "Docker не обнаружен — не требуется для работы приложения.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi не найден. Проверьте, что установлен драйвер NVIDIA.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi завершился с ошибкой (код {0}): {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0}: доступно {1} МБ видеопамяти — недостаточно даже для самого лёгкого tier " +
                "({2} МБ). GPU будет исключена из майнинга.",
            ["Tier.AutoAssigned"] = "{0}: доступно {1} МБ → назначен tier \"{2}\" (требуется {3} МБ).",
            ["Tier.ManualRisky"] =
                "{0}: выбран tier \"{1}\" ({2} МБ), но доступно только {3} МБ. Высокая вероятность " +
                "нехватки видеопамяти (OOM) при запуске.",
            ["Tier.ManualFits"] = "{0}: tier \"{1}\" укладывается в доступную видеопамять ({2} МБ).",

            ["ModelDownload.ChecksumMismatch"] =
                "Контрольная сумма не совпадает: ожидалось {0}, получено {1}. Файл удалён - похоже, " +
                "скачивание повреждено или URL указывает не на тот файл.",

            ["Process.AlreadyRunning"] =
                "Уже запущено — повторный запуск проигнорирован (защита от двойного старта).",
            ["Process.NodeStarted"] = "Нода запущена (PID {0}).",
            ["Process.MinerStarted"] = "Майнер запущен (PID {0}).",
            ["Process.StoppedByUser"] = "Остановлено по запросу пользователя.",
            ["Process.RestartLimitReached"] =
                "Достигнут лимит автоматических перезапусков. Требуется ручное вмешательство.",
            ["Process.RestartingSoon"] = "Процесс завершился неожиданно. Перезапуск через {0} сек (попытка {1}).",
            ["Process.Restarted"] = "Перезапущено (PID {0}).",
            ["Process.RestartFailed"] = "Не удалось перезапустить: {0}",

            ["Runtime.ExecutableNotFound"] = "Исполняемый файл не найден: {0}",

            ["Safety.Critical"] =
                "{0}: критическая температура {1}°C (порог {2}°C) - майнинг остановлен для защиты оборудования.",
            ["Safety.Warning"] = "{0}: высокая температура {1}°C (порог {2}°C).",
            ["Safety.Normal"] = "{0}: температура в норме ({1}°C).",

            ["Path.Empty"] = "Путь не может быть пустым.",
            ["Path.InvalidChars"] = "Путь содержит недопустимые символы.",
            ["Path.NotAbsolute"] = "Укажите абсолютный путь.",
            ["Path.InvalidPath"] = "Некорректный путь: {0}",
            ["Path.ProtectedRoot"] = "Нельзя использовать системную папку ({0}) для данных Keryx.",

            ["Update.ReleaseCheckFailed"] = "Не удалось проверить обновления {0} (код {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub вернул пустой ответ при проверке обновлений {0}.",
            ["Update.ExeNotFoundInArchive"] = "Не удалось найти исполняемый файл ({0}) в скачанном архиве.",
            ["Update.ExtractedExeMissing"] = "Извлечённый файл не найден: {0}",
        },
        ["en"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " Task Scheduler refused the request to create the task. This normally doesn't " +
                "require administrator rights - if the error keeps happening, try running this " +
                "application as Administrator (right-click → \"Run as administrator\"); if that " +
                "doesn't help either, check your antivirus/security software.",
            ["TaskScheduler.RegisterFailed"] = "Failed to create the autostart task (code {0}): {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "Failed to remove the autostart task (code {0}): {1}",

            ["Profile.NotFound"] = "Profile \"{0}\" not found.",
            ["Profile.NameEmpty"] = "The profile name cannot be empty.",
            ["Profile.AlreadyExists"] = "Profile \"{0}\" already exists.",
            ["Profile.CannotDeleteLast"] = "The last remaining profile cannot be deleted.",

            ["SystemChecker.WindowsVersionName"] = "Windows version",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0} - supported.",
            ["SystemChecker.WindowsVersionTooOld"] = "Detected {0}. Windows 10 or newer is required.",
            ["SystemChecker.GpuName"] = "NVIDIA GPU",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi ran successfully but reported no GPUs.",
            ["SystemChecker.GpuFound"] = "Found: {0}",
            ["SystemChecker.WslName"] = "WSL (optional)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe failed to start.",
            ["SystemChecker.WslTimeout"] =
                "The WSL check didn't finish in time - this doesn't affect the app (the native backend is used).",
            ["SystemChecker.WslDetected"] = "WSL detected and available.",
            ["SystemChecker.WslNotDetected"] =
                "WSL not detected or not configured - not required: the app uses the native Windows backend by default.",
            ["SystemChecker.WslNotFound"] =
                "wsl.exe was not found on this system - not required: the app uses the native Windows backend by default.",
            ["SystemChecker.DockerName"] = "Docker (optional)",
            ["SystemChecker.DockerFound"] = "docker.exe found on PATH.",
            ["SystemChecker.DockerNotFound"] = "Docker not detected - not required for the app to work.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi not found. Check that the NVIDIA driver is installed.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi exited with an error (code {0}): {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0}: {1} MB of VRAM available - not enough even for the lightest tier ({2} MB). " +
                "This GPU will be excluded from mining.",
            ["Tier.AutoAssigned"] = "{0}: {1} MB available → assigned tier \"{2}\" (requires {3} MB).",
            ["Tier.ManualRisky"] =
                "{0}: tier \"{1}\" selected ({2} MB), but only {3} MB is available. High risk of " +
                "running out of VRAM (OOM) at launch.",
            ["Tier.ManualFits"] = "{0}: tier \"{1}\" fits within the available VRAM ({2} MB).",

            ["ModelDownload.ChecksumMismatch"] =
                "Checksum mismatch: expected {0}, got {1}. The file was deleted - the download " +
                "appears corrupted, or the URL points to the wrong file.",

            ["Process.AlreadyRunning"] = "Already running - repeat launch ignored (double-start protection).",
            ["Process.NodeStarted"] = "Node started (PID {0}).",
            ["Process.MinerStarted"] = "Miner started (PID {0}).",
            ["Process.StoppedByUser"] = "Stopped at the user's request.",
            ["Process.RestartLimitReached"] = "Automatic restart limit reached. Manual intervention required.",
            ["Process.RestartingSoon"] = "The process exited unexpectedly. Restarting in {0}s (attempt {1}).",
            ["Process.Restarted"] = "Restarted (PID {0}).",
            ["Process.RestartFailed"] = "Failed to restart: {0}",

            ["Runtime.ExecutableNotFound"] = "Executable not found: {0}",

            ["Safety.Critical"] =
                "{0}: critical temperature {1}°C (threshold {2}°C) - mining stopped to protect the hardware.",
            ["Safety.Warning"] = "{0}: high temperature {1}°C (threshold {2}°C).",
            ["Safety.Normal"] = "{0}: temperature normal ({1}°C).",

            ["Path.Empty"] = "The path cannot be empty.",
            ["Path.InvalidChars"] = "The path contains invalid characters.",
            ["Path.NotAbsolute"] = "Please specify an absolute path.",
            ["Path.InvalidPath"] = "Invalid path: {0}",
            ["Path.ProtectedRoot"] = "The system folder ({0}) cannot be used for Keryx data.",

            ["Update.ReleaseCheckFailed"] = "Failed to check for {0} updates (code {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub returned an empty response while checking for {0} updates.",
            ["Update.ExeNotFoundInArchive"] = "Could not find an executable ({0}) in the downloaded archive.",
            ["Update.ExtractedExeMissing"] = "Extracted file not found: {0}",
        },
        ["es"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " El Programador de tareas rechazó la solicitud para crear la tarea. Normalmente " +
                "esto no requiere permisos de administrador - si el error persiste, intente " +
                "ejecutar esta aplicación como administrador (clic derecho → \"Ejecutar como " +
                "administrador\"); si eso tampoco ayuda, revise su antivirus/software de seguridad.",
            ["TaskScheduler.RegisterFailed"] = "No se pudo crear la tarea de inicio automático (código {0}): {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "No se pudo eliminar la tarea de inicio automático (código {0}): {1}",

            ["Profile.NotFound"] = "Perfil «{0}» no encontrado.",
            ["Profile.NameEmpty"] = "El nombre del perfil no puede estar vacío.",
            ["Profile.AlreadyExists"] = "El perfil «{0}» ya existe.",
            ["Profile.CannotDeleteLast"] = "No se puede eliminar el último perfil restante.",

            ["SystemChecker.WindowsVersionName"] = "Versión de Windows",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0}: compatible.",
            ["SystemChecker.WindowsVersionTooOld"] = "Se detectó {0}. Se requiere Windows 10 o posterior.",
            ["SystemChecker.GpuName"] = "GPU NVIDIA",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi se ejecutó correctamente pero no informó ninguna GPU.",
            ["SystemChecker.GpuFound"] = "Encontrado: {0}",
            ["SystemChecker.WslName"] = "WSL (opcional)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe no pudo iniciarse.",
            ["SystemChecker.WslTimeout"] =
                "La comprobación de WSL no terminó a tiempo - esto no afecta a la aplicación (se usa el backend nativo).",
            ["SystemChecker.WslDetected"] = "WSL detectado y disponible.",
            ["SystemChecker.WslNotDetected"] =
                "WSL no detectado o no configurado - no es necesario: la aplicación usa el backend nativo de Windows por defecto.",
            ["SystemChecker.WslNotFound"] =
                "No se encontró wsl.exe en este sistema - no es necesario: la aplicación usa el backend nativo de Windows por defecto.",
            ["SystemChecker.DockerName"] = "Docker (opcional)",
            ["SystemChecker.DockerFound"] = "docker.exe encontrado en el PATH.",
            ["SystemChecker.DockerNotFound"] = "Docker no detectado - no es necesario para el funcionamiento de la app.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi no encontrado. Verifique que el controlador NVIDIA esté instalado.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi finalizó con un error (código {0}): {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0}: {1} MB de VRAM disponible - insuficiente incluso para el tier más ligero " +
                "({2} MB). Esta GPU será excluida de la minería.",
            ["Tier.AutoAssigned"] = "{0}: {1} MB disponibles → tier asignado \"{2}\" (requiere {3} MB).",
            ["Tier.ManualRisky"] =
                "{0}: tier \"{1}\" seleccionado ({2} MB), pero solo hay {3} MB disponibles. Alto " +
                "riesgo de quedarse sin VRAM (OOM) al iniciar.",
            ["Tier.ManualFits"] = "{0}: el tier \"{1}\" cabe en la VRAM disponible ({2} MB).",

            ["ModelDownload.ChecksumMismatch"] =
                "La suma de comprobación no coincide: se esperaba {0}, se obtuvo {1}. El archivo " +
                "fue eliminado - la descarga parece estar dañada, o la URL apunta al archivo equivocado.",

            ["Process.AlreadyRunning"] = "Ya en ejecución - se ignoró el reinicio (protección contra doble inicio).",
            ["Process.NodeStarted"] = "Nodo iniciado (PID {0}).",
            ["Process.MinerStarted"] = "Minero iniciado (PID {0}).",
            ["Process.StoppedByUser"] = "Detenido a petición del usuario.",
            ["Process.RestartLimitReached"] = "Se alcanzó el límite de reinicios automáticos. Se requiere intervención manual.",
            ["Process.RestartingSoon"] = "El proceso finalizó inesperadamente. Reiniciando en {0}s (intento {1}).",
            ["Process.Restarted"] = "Reiniciado (PID {0}).",
            ["Process.RestartFailed"] = "No se pudo reiniciar: {0}",

            ["Runtime.ExecutableNotFound"] = "Ejecutable no encontrado: {0}",

            ["Safety.Critical"] =
                "{0}: temperatura crítica {1}°C (umbral {2}°C) - minería detenida para proteger el hardware.",
            ["Safety.Warning"] = "{0}: temperatura alta {1}°C (umbral {2}°C).",
            ["Safety.Normal"] = "{0}: temperatura normal ({1}°C).",

            ["Path.Empty"] = "La ruta no puede estar vacía.",
            ["Path.InvalidChars"] = "La ruta contiene caracteres no válidos.",
            ["Path.NotAbsolute"] = "Especifique una ruta absoluta.",
            ["Path.InvalidPath"] = "Ruta no válida: {0}",
            ["Path.ProtectedRoot"] = "No se puede usar la carpeta del sistema ({0}) para los datos de Keryx.",

            ["Update.ReleaseCheckFailed"] = "No se pudieron comprobar actualizaciones de {0} (código {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub devolvió una respuesta vacía al comprobar actualizaciones de {0}.",
            ["Update.ExeNotFoundInArchive"] = "No se encontró un ejecutable ({0}) en el archivo descargado.",
            ["Update.ExtractedExeMissing"] = "Archivo extraído no encontrado: {0}",
        },
        ["it"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " L'Utilità di pianificazione ha rifiutato la richiesta di creare l'attività. " +
                "Normalmente non richiede diritti di amministratore - se l'errore persiste, provi " +
                "a eseguire questa applicazione come amministratore (clic destro → \"Esegui come " +
                "amministratore\"); se non risolve, controlli l'antivirus/software di sicurezza.",
            ["TaskScheduler.RegisterFailed"] = "Impossibile creare l'attività di avvio automatico (codice {0}): {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "Impossibile rimuovere l'attività di avvio automatico (codice {0}): {1}",

            ["Profile.NotFound"] = "Profilo «{0}» non trovato.",
            ["Profile.NameEmpty"] = "Il nome del profilo non può essere vuoto.",
            ["Profile.AlreadyExists"] = "Il profilo «{0}» esiste già.",
            ["Profile.CannotDeleteLast"] = "Impossibile eliminare l'ultimo profilo rimasto.",

            ["SystemChecker.WindowsVersionName"] = "Versione di Windows",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0} - supportato.",
            ["SystemChecker.WindowsVersionTooOld"] = "Rilevato {0}. È richiesto Windows 10 o successivo.",
            ["SystemChecker.GpuName"] = "GPU NVIDIA",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi è stato eseguito correttamente ma non ha segnalato alcuna GPU.",
            ["SystemChecker.GpuFound"] = "Trovato: {0}",
            ["SystemChecker.WslName"] = "WSL (opzionale)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe non è riuscito ad avviarsi.",
            ["SystemChecker.WslTimeout"] =
                "Il controllo WSL non è terminato in tempo - non influisce sull'app (viene usato il backend nativo).",
            ["SystemChecker.WslDetected"] = "WSL rilevato e disponibile.",
            ["SystemChecker.WslNotDetected"] =
                "WSL non rilevato o non configurato - non necessario: l'app usa il backend nativo di Windows per impostazione predefinita.",
            ["SystemChecker.WslNotFound"] =
                "wsl.exe non trovato su questo sistema - non necessario: l'app usa il backend nativo di Windows per impostazione predefinita.",
            ["SystemChecker.DockerName"] = "Docker (opzionale)",
            ["SystemChecker.DockerFound"] = "docker.exe trovato nel PATH.",
            ["SystemChecker.DockerNotFound"] = "Docker non rilevato - non necessario per il funzionamento dell'app.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi non trovato. Verificare che il driver NVIDIA sia installato.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi terminato con un errore (codice {0}): {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0}: {1} MB di VRAM disponibile - insufficiente anche per il tier più leggero " +
                "({2} MB). Questa GPU sarà esclusa dal mining.",
            ["Tier.AutoAssigned"] = "{0}: {1} MB disponibili → tier assegnato \"{2}\" (richiede {3} MB).",
            ["Tier.ManualRisky"] =
                "{0}: tier \"{1}\" selezionato ({2} MB), ma solo {3} MB disponibili. Alto rischio " +
                "di esaurimento della VRAM (OOM) all'avvio.",
            ["Tier.ManualFits"] = "{0}: il tier \"{1}\" rientra nella VRAM disponibile ({2} MB).",

            ["ModelDownload.ChecksumMismatch"] =
                "Checksum non corrispondente: atteso {0}, ottenuto {1}. Il file è stato eliminato - " +
                "il download sembra corrotto, oppure l'URL punta al file sbagliato.",

            ["Process.AlreadyRunning"] = "Già in esecuzione - riavvio ignorato (protezione contro il doppio avvio).",
            ["Process.NodeStarted"] = "Nodo avviato (PID {0}).",
            ["Process.MinerStarted"] = "Miner avviato (PID {0}).",
            ["Process.StoppedByUser"] = "Arrestato su richiesta dell'utente.",
            ["Process.RestartLimitReached"] = "Raggiunto il limite di riavvii automatici. È richiesto un intervento manuale.",
            ["Process.RestartingSoon"] = "Il processo è terminato inaspettatamente. Riavvio tra {0}s (tentativo {1}).",
            ["Process.Restarted"] = "Riavviato (PID {0}).",
            ["Process.RestartFailed"] = "Impossibile riavviare: {0}",

            ["Runtime.ExecutableNotFound"] = "Eseguibile non trovato: {0}",

            ["Safety.Critical"] =
                "{0}: temperatura critica {1}°C (soglia {2}°C) - mining arrestato per proteggere l'hardware.",
            ["Safety.Warning"] = "{0}: temperatura alta {1}°C (soglia {2}°C).",
            ["Safety.Normal"] = "{0}: temperatura normale ({1}°C).",

            ["Path.Empty"] = "Il percorso non può essere vuoto.",
            ["Path.InvalidChars"] = "Il percorso contiene caratteri non validi.",
            ["Path.NotAbsolute"] = "Specificare un percorso assoluto.",
            ["Path.InvalidPath"] = "Percorso non valido: {0}",
            ["Path.ProtectedRoot"] = "Impossibile usare la cartella di sistema ({0}) per i dati di Keryx.",

            ["Update.ReleaseCheckFailed"] = "Impossibile verificare gli aggiornamenti di {0} (codice {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub ha restituito una risposta vuota durante la verifica degli aggiornamenti di {0}.",
            ["Update.ExeNotFoundInArchive"] = "Impossibile trovare un eseguibile ({0}) nell'archivio scaricato.",
            ["Update.ExtractedExeMissing"] = "File estratto non trovato: {0}",
        },
        ["fr"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " Le Planificateur de tâches a refusé la demande de création de la tâche. Cela ne " +
                "nécessite normalement pas de droits d'administrateur - si l'erreur persiste, " +
                "essayez d'exécuter cette application en tant qu'administrateur (clic droit → " +
                "\"Exécuter en tant qu'administrateur\"); si cela ne fonctionne pas non plus, " +
                "vérifiez votre antivirus/logiciel de sécurité.",
            ["TaskScheduler.RegisterFailed"] = "Échec de la création de la tâche de démarrage automatique (code {0}) : {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "Échec de la suppression de la tâche de démarrage automatique (code {0}) : {1}",

            ["Profile.NotFound"] = "Profil « {0} » introuvable.",
            ["Profile.NameEmpty"] = "Le nom du profil ne peut pas être vide.",
            ["Profile.AlreadyExists"] = "Le profil « {0} » existe déjà.",
            ["Profile.CannotDeleteLast"] = "Impossible de supprimer le dernier profil restant.",

            ["SystemChecker.WindowsVersionName"] = "Version de Windows",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0} - pris en charge.",
            ["SystemChecker.WindowsVersionTooOld"] = "{0} détecté. Windows 10 ou plus récent est requis.",
            ["SystemChecker.GpuName"] = "GPU NVIDIA",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi s'est exécuté correctement mais n'a signalé aucun GPU.",
            ["SystemChecker.GpuFound"] = "Trouvé : {0}",
            ["SystemChecker.WslName"] = "WSL (facultatif)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe n'a pas pu démarrer.",
            ["SystemChecker.WslTimeout"] =
                "La vérification de WSL n'a pas abouti à temps - cela n'affecte pas l'application (le backend natif est utilisé).",
            ["SystemChecker.WslDetected"] = "WSL détecté et disponible.",
            ["SystemChecker.WslNotDetected"] =
                "WSL non détecté ou non configuré - non requis : l'application utilise le backend Windows natif par défaut.",
            ["SystemChecker.WslNotFound"] =
                "wsl.exe introuvable sur ce système - non requis : l'application utilise le backend Windows natif par défaut.",
            ["SystemChecker.DockerName"] = "Docker (facultatif)",
            ["SystemChecker.DockerFound"] = "docker.exe trouvé dans le PATH.",
            ["SystemChecker.DockerNotFound"] = "Docker non détecté - non requis pour le fonctionnement de l'application.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi introuvable. Vérifiez que le pilote NVIDIA est installé.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi s'est terminé avec une erreur (code {0}) : {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0} : {1} Mo de VRAM disponible - insuffisant même pour le tier le plus léger " +
                "({2} Mo). Ce GPU sera exclu du minage.",
            ["Tier.AutoAssigned"] = "{0} : {1} Mo disponibles → tier assigné \"{2}\" (nécessite {3} Mo).",
            ["Tier.ManualRisky"] =
                "{0} : tier \"{1}\" sélectionné ({2} Mo), mais seulement {3} Mo disponibles. Risque " +
                "élevé de manquer de VRAM (OOM) au démarrage.",
            ["Tier.ManualFits"] = "{0} : le tier \"{1}\" tient dans la VRAM disponible ({2} Mo).",

            ["ModelDownload.ChecksumMismatch"] =
                "La somme de contrôle ne correspond pas : attendu {0}, obtenu {1}. Le fichier a été " +
                "supprimé - le téléchargement semble corrompu, ou l'URL pointe vers le mauvais fichier.",

            ["Process.AlreadyRunning"] = "Déjà en cours d'exécution - nouveau démarrage ignoré (protection contre le double démarrage).",
            ["Process.NodeStarted"] = "Nœud démarré (PID {0}).",
            ["Process.MinerStarted"] = "Mineur démarré (PID {0}).",
            ["Process.StoppedByUser"] = "Arrêté à la demande de l'utilisateur.",
            ["Process.RestartLimitReached"] = "Limite de redémarrages automatiques atteinte. Une intervention manuelle est requise.",
            ["Process.RestartingSoon"] = "Le processus s'est arrêté de manière inattendue. Redémarrage dans {0}s (tentative {1}).",
            ["Process.Restarted"] = "Redémarré (PID {0}).",
            ["Process.RestartFailed"] = "Échec du redémarrage : {0}",

            ["Runtime.ExecutableNotFound"] = "Exécutable introuvable : {0}",

            ["Safety.Critical"] =
                "{0} : température critique {1}°C (seuil {2}°C) - minage arrêté pour protéger le matériel.",
            ["Safety.Warning"] = "{0} : température élevée {1}°C (seuil {2}°C).",
            ["Safety.Normal"] = "{0} : température normale ({1}°C).",

            ["Path.Empty"] = "Le chemin ne peut pas être vide.",
            ["Path.InvalidChars"] = "Le chemin contient des caractères non valides.",
            ["Path.NotAbsolute"] = "Veuillez indiquer un chemin absolu.",
            ["Path.InvalidPath"] = "Chemin non valide : {0}",
            ["Path.ProtectedRoot"] = "Impossible d'utiliser le dossier système ({0}) pour les données Keryx.",

            ["Update.ReleaseCheckFailed"] = "Impossible de vérifier les mises à jour de {0} (code {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub a renvoyé une réponse vide lors de la vérification des mises à jour de {0}.",
            ["Update.ExeNotFoundInArchive"] = "Impossible de trouver un exécutable ({0}) dans l'archive téléchargée.",
            ["Update.ExtractedExeMissing"] = "Fichier extrait introuvable : {0}",
        },
        ["uk"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " Планувальник завдань відхилив запит на створення задачі. Зазвичай це не потребує " +
                "прав адміністратора - якщо помилка повторюється, спробуйте запустити цей застосунок " +
                "від імені адміністратора (правий клік → «Запуск від імені адміністратора»); якщо це " +
                "не допомагає, перевірте антивірус/захисне ПЗ.",
            ["TaskScheduler.RegisterFailed"] = "Не вдалося створити задачу автозапуску (код {0}): {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "Не вдалося видалити задачу автозапуску (код {0}): {1}",

            ["Profile.NotFound"] = "Профіль «{0}» не знайдено.",
            ["Profile.NameEmpty"] = "Ім'я профілю не може бути порожнім.",
            ["Profile.AlreadyExists"] = "Профіль «{0}» вже існує.",
            ["Profile.CannotDeleteLast"] = "Не можна видалити останній профіль, що залишився.",

            ["SystemChecker.WindowsVersionName"] = "Версія Windows",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0} — підтримується.",
            ["SystemChecker.WindowsVersionTooOld"] = "Виявлено {0}. Потрібна Windows 10 або новіша.",
            ["SystemChecker.GpuName"] = "Відеокарта NVIDIA",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi відпрацював, але не повідомив жодної відеокарти.",
            ["SystemChecker.GpuFound"] = "Знайдено: {0}",
            ["SystemChecker.WslName"] = "WSL (необов'язково)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe не запустився.",
            ["SystemChecker.WslTimeout"] =
                "Перевірка WSL не завершилась вчасно — не впливає на роботу застосунку (використовується нативний режим).",
            ["SystemChecker.WslDetected"] = "WSL виявлено і доступний.",
            ["SystemChecker.WslNotDetected"] =
                "WSL не виявлено або не налаштовано — не потрібно: застосунок за замовчуванням використовує нативний режим Windows.",
            ["SystemChecker.WslNotFound"] =
                "wsl.exe не знайдено в системі — не потрібно: застосунок за замовчуванням використовує нативний режим Windows.",
            ["SystemChecker.DockerName"] = "Docker (необов'язково)",
            ["SystemChecker.DockerFound"] = "docker.exe знайдено в PATH.",
            ["SystemChecker.DockerNotFound"] = "Docker не виявлено — не потрібен для роботи застосунку.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi не знайдено. Перевірте, що встановлено драйвер NVIDIA.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi завершився з помилкою (код {0}): {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0}: доступно {1} МБ відеопам'яті — недостатньо навіть для найлегшого tier " +
                "({2} МБ). GPU буде виключено з майнінгу.",
            ["Tier.AutoAssigned"] = "{0}: доступно {1} МБ → призначено tier \"{2}\" (потрібно {3} МБ).",
            ["Tier.ManualRisky"] =
                "{0}: обрано tier \"{1}\" ({2} МБ), але доступно лише {3} МБ. Висока ймовірність " +
                "нехватки відеопам'яті (OOM) під час запуску.",
            ["Tier.ManualFits"] = "{0}: tier \"{1}\" вкладається в доступну відеопам'ять ({2} МБ).",

            ["ModelDownload.ChecksumMismatch"] =
                "Контрольна сума не збігається: очікувалося {0}, отримано {1}. Файл видалено - " +
                "схоже, завантаження пошкоджено або URL вказує не на той файл.",

            ["Process.AlreadyRunning"] = "Вже запущено — повторний запуск проігноровано (захист від подвійного старту).",
            ["Process.NodeStarted"] = "Ноду запущено (PID {0}).",
            ["Process.MinerStarted"] = "Майнер запущено (PID {0}).",
            ["Process.StoppedByUser"] = "Зупинено за запитом користувача.",
            ["Process.RestartLimitReached"] = "Досягнуто ліміт автоматичних перезапусків. Потрібне ручне втручання.",
            ["Process.RestartingSoon"] = "Процес завершився несподівано. Перезапуск через {0} с (спроба {1}).",
            ["Process.Restarted"] = "Перезапуск виконано (PID {0}).",
            ["Process.RestartFailed"] = "Не вдалося перезапустити: {0}",

            ["Runtime.ExecutableNotFound"] = "Виконуваний файл не знайдено: {0}",

            ["Safety.Critical"] =
                "{0}: критична температура {1}°C (поріг {2}°C) - майнінг зупинено для захисту обладнання.",
            ["Safety.Warning"] = "{0}: висока температура {1}°C (поріг {2}°C).",
            ["Safety.Normal"] = "{0}: температура в нормі ({1}°C).",

            ["Path.Empty"] = "Шлях не може бути порожнім.",
            ["Path.InvalidChars"] = "Шлях містить недопустимі символи.",
            ["Path.NotAbsolute"] = "Вкажіть абсолютний шлях.",
            ["Path.InvalidPath"] = "Некоректний шлях: {0}",
            ["Path.ProtectedRoot"] = "Не можна використовувати системну папку ({0}) для даних Keryx.",

            ["Update.ReleaseCheckFailed"] = "Не вдалося перевірити оновлення {0} (код {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub повернув порожню відповідь під час перевірки оновлень {0}.",
            ["Update.ExeNotFoundInArchive"] = "Не вдалося знайти виконуваний файл ({0}) у завантаженому архіві.",
            ["Update.ExtractedExeMissing"] = "Видобутий файл не знайдено: {0}",
        },
        ["de"] = new Dictionary<string, string>
        {
            ["TaskScheduler.AccessDeniedHint"] =
                " Die Aufgabenplanung hat die Anfrage zum Erstellen der Aufgabe abgelehnt. Normalerweise " +
                "sind dafür keine Administratorrechte erforderlich - wenn der Fehler weiterhin auftritt, " +
                "versuchen Sie, diese Anwendung als Administrator auszuführen (Rechtsklick → " +
                "\"Als Administrator ausführen\"); wenn das auch nicht hilft, überprüfen Sie Ihre " +
                "Antiviren-/Sicherheitssoftware.",
            ["TaskScheduler.RegisterFailed"] = "Autostart-Aufgabe konnte nicht erstellt werden (Code {0}): {1}{2}",
            ["TaskScheduler.UnregisterFailed"] = "Autostart-Aufgabe konnte nicht entfernt werden (Code {0}): {1}",

            ["Profile.NotFound"] = "Profil „{0}“ wurde nicht gefunden.",
            ["Profile.NameEmpty"] = "Der Profilname darf nicht leer sein.",
            ["Profile.AlreadyExists"] = "Profil „{0}“ existiert bereits.",
            ["Profile.CannotDeleteLast"] = "Das letzte verbleibende Profil kann nicht gelöscht werden.",

            ["SystemChecker.WindowsVersionName"] = "Windows-Version",
            ["SystemChecker.WindowsVersionOk"] = "Windows {0} - unterstützt.",
            ["SystemChecker.WindowsVersionTooOld"] = "{0} erkannt. Windows 10 oder neuer wird benötigt.",
            ["SystemChecker.GpuName"] = "NVIDIA-GPU",
            ["SystemChecker.GpuNoneFound"] = "nvidia-smi wurde erfolgreich ausgeführt, meldete aber keine GPU.",
            ["SystemChecker.GpuFound"] = "Gefunden: {0}",
            ["SystemChecker.WslName"] = "WSL (optional)",
            ["SystemChecker.WslNotStarted"] = "wsl.exe konnte nicht gestartet werden.",
            ["SystemChecker.WslTimeout"] =
                "Die WSL-Prüfung wurde nicht rechtzeitig abgeschlossen - dies wirkt sich nicht auf die App aus (natives Backend wird verwendet).",
            ["SystemChecker.WslDetected"] = "WSL erkannt und verfügbar.",
            ["SystemChecker.WslNotDetected"] =
                "WSL nicht erkannt oder nicht konfiguriert - nicht erforderlich: die App verwendet standardmäßig das native Windows-Backend.",
            ["SystemChecker.WslNotFound"] =
                "wsl.exe wurde auf diesem System nicht gefunden - nicht erforderlich: die App verwendet standardmäßig das native Windows-Backend.",
            ["SystemChecker.DockerName"] = "Docker (optional)",
            ["SystemChecker.DockerFound"] = "docker.exe im PATH gefunden.",
            ["SystemChecker.DockerNotFound"] = "Docker nicht erkannt - für die App nicht erforderlich.",

            ["Gpu.NvidiaSmiNotFound"] = "nvidia-smi nicht gefunden. Prüfen Sie, ob der NVIDIA-Treiber installiert ist.",
            ["Gpu.NvidiaSmiFailed"] = "nvidia-smi wurde mit einem Fehler beendet (Code {0}): {1}",

            ["Tier.ExcludedInsufficientVram"] =
                "{0}: {1} MB VRAM verfügbar - nicht einmal für die leichteste Stufe ausreichend " +
                "({2} MB). Diese GPU wird vom Mining ausgeschlossen.",
            ["Tier.AutoAssigned"] = "{0}: {1} MB verfügbar → Stufe \"{2}\" zugewiesen (benötigt {3} MB).",
            ["Tier.ManualRisky"] =
                "{0}: Stufe \"{1}\" ausgewählt ({2} MB), aber nur {3} MB verfügbar. Hohes Risiko " +
                "eines VRAM-Mangels (OOM) beim Start.",
            ["Tier.ManualFits"] = "{0}: Stufe \"{1}\" passt in den verfügbaren VRAM ({2} MB).",

            ["ModelDownload.ChecksumMismatch"] =
                "Prüfsummenfehler: erwartet {0}, erhalten {1}. Die Datei wurde gelöscht - der Download " +
                "scheint beschädigt zu sein, oder die URL verweist auf die falsche Datei.",

            ["Process.AlreadyRunning"] = "Bereits gestartet - erneuter Start ignoriert (Schutz vor Doppelstart).",
            ["Process.NodeStarted"] = "Node gestartet (PID {0}).",
            ["Process.MinerStarted"] = "Miner gestartet (PID {0}).",
            ["Process.StoppedByUser"] = "Auf Anforderung des Benutzers gestoppt.",
            ["Process.RestartLimitReached"] = "Limit für automatische Neustarts erreicht. Manuelles Eingreifen erforderlich.",
            ["Process.RestartingSoon"] = "Der Prozess wurde unerwartet beendet. Neustart in {0}s (Versuch {1}).",
            ["Process.Restarted"] = "Neu gestartet (PID {0}).",
            ["Process.RestartFailed"] = "Neustart fehlgeschlagen: {0}",

            ["Runtime.ExecutableNotFound"] = "Ausführbare Datei nicht gefunden: {0}",

            ["Safety.Critical"] =
                "{0}: kritische Temperatur {1}°C (Schwelle {2}°C) - Mining zum Schutz der Hardware gestoppt.",
            ["Safety.Warning"] = "{0}: hohe Temperatur {1}°C (Schwelle {2}°C).",
            ["Safety.Normal"] = "{0}: Temperatur normal ({1}°C).",

            ["Path.Empty"] = "Der Pfad darf nicht leer sein.",
            ["Path.InvalidChars"] = "Der Pfad enthält ungültige Zeichen.",
            ["Path.NotAbsolute"] = "Bitte geben Sie einen absoluten Pfad an.",
            ["Path.InvalidPath"] = "Ungültiger Pfad: {0}",
            ["Path.ProtectedRoot"] = "Der Systemordner ({0}) kann nicht für Keryx-Daten verwendet werden.",

            ["Update.ReleaseCheckFailed"] = "Updates für {0} konnten nicht überprüft werden (Code {1}).",
            ["Update.ReleaseCheckEmptyResponse"] = "GitHub hat bei der Update-Prüfung für {0} eine leere Antwort zurückgegeben.",
            ["Update.ExeNotFoundInArchive"] = "In dem heruntergeladenen Archiv wurde keine ausführbare Datei ({0}) gefunden.",
            ["Update.ExtractedExeMissing"] = "Extrahierte Datei nicht gefunden: {0}",
        },
    };
}
