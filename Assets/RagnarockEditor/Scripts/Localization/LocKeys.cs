namespace RagnarockEditor.Localization
{
    /// <summary>
    /// Константы ключей локализации. Держим их в одном месте, чтобы опечатка в ключе
    /// была ошибкой компиляции, а не пустой строкой в шлеме.
    /// Значения обязаны совпадать с ключами в Resources/Localization/*.json.
    /// </summary>
    public static class LocKeys
    {
        // ------------------------------------------------------------ приложение
        public const string AppTitle = "app.title";
        public const string AppSubtitle = "app.subtitle";

        // --------------------------------------------------------------- интро
        public const string IntroTitle = "intro.title";
        public const string IntroBody = "intro.body";
        public const string IntroLanguage = "intro.language";
        public const string IntroStart = "intro.start";
        public const string IntroClose = "intro.close";

        // --------------------------------------------------------- главное меню
        public const string MenuNewLevel = "menu.newLevel";
        public const string MenuChooseSong = "menu.chooseSong";
        public const string MenuHelp = "menu.help";
        public const string MenuSettings = "menu.settings";
        public const string MenuExit = "menu.exit";

        // ------------------------------------------------------------ выбор песни
        public const string SongsTitle = "songs.title";
        public const string SongsListHint = "songs.listHint";
        public const string SongsCount = "songs.count";
        public const string SongsEmpty = "songs.empty";
        public const string SongsPermissionWait = "songs.permissionWait";
        public const string SongsPermissionHint = "songs.permissionHint";
        public const string SongsGrant = "songs.grant";
        public const string SongsBack = "songs.back";
        public const string SongsPage = "songs.page";
        public const string SongsLoading = "songs.loading";
        public const string SongsAnalyzing = "songs.analyzing";
        public const string SongsAnalysisError = "songs.analysisError";

        // ------------------------------------------------------------------ BPM
        public const string BpmTitle = "bpm.title";
        public const string BpmValue = "bpm.value";
        public const string BpmConfidence = "bpm.confidence";
        public const string BpmLowConfidence = "bpm.lowConfidence";
        public const string BpmTap = "bpm.tap";
        public const string BpmTaps = "bpm.taps";
        public const string BpmTapValue = "bpm.tapValue";
        public const string BpmContinue = "bpm.continue";
        public const string BpmBack = "bpm.back";
        public const string BpmStatus = "bpm.status";

        // --------------------------------------------------------------- запись
        public const string RecordNoSongTitle = "record.noSong.title";
        public const string RecordNoSongBody = "record.noSong.body";
        public const string RecordNoSongChoose = "record.noSong.choose";
        public const string RecordNoSongFreeplay = "record.noSong.freeplay";
        public const string RecordReadyTitle = "record.ready.title";
        public const string RecordReadyHint = "record.ready.hint";
        public const string RecordReadyAnother = "record.ready.another";
        public const string RecordReadyStart = "record.ready.start";
        public const string RecordCountdown = "record.countdown";
        public const string RecordBpm = "record.bpm";
        public const string RecordFinishedTitle = "record.finished.title";
        public const string RecordFinishedSummary = "record.finished.summary";
        public const string RecordFinishedExport = "record.finished.export";
        public const string RecordFinishedPreview = "record.finished.preview";
        public const string RecordFinishedRetake = "record.finished.retake";
        public const string RecordFinishedAnother = "record.finished.another";

        // ------------------------------------------------------------------ HUD
        public const string HudHits = "hud.hits";
        public const string HudHitsPerDrum = "hud.hitsPerDrum";
        public const string HudRewind = "hud.rewind";
        public const string HudPause = "hud.pause";
        public const string HudResume = "hud.resume";
        public const string HudStop = "hud.stop";
        public const string HudMenu = "hud.menu";

        // -------------------------------------------------------- подписи кнопок
        public const string ControlsPlay = "controls.play";
        public const string ControlsPause = "controls.pause";
        public const string ControlsStop = "controls.stop";
        public const string ControlsRestart = "controls.restart";
        public const string ControlsExport = "controls.export";
        public const string ControlsCalibrate = "controls.calibrate";
        public const string ControlsBack = "controls.back";
        public const string ControlsHelp = "controls.help";

        // -------------------------------------------------------- выход/диалоги
        public const string ExitTitle = "exit.title";
        public const string ExitMessage = "exit.message";
        public const string ExitUnsavedTitle = "exit.unsavedTitle";
        public const string ExitUnsavedMessage = "exit.unsavedMessage";
        public const string ExitLeave = "exit.leave";
        public const string ExitSaveAndLeave = "exit.saveAndLeave";
        public const string ExitCancel = "exit.cancel";
        public const string ExitExportFailed = "exit.exportFailed";

        // --------------------------------------------------------------- экспорт
        public const string ExportWorking = "export.working";
        public const string ExportSuccess = "export.success";
        public const string ExportFailed = "export.failed";
        public const string ExportNoHits = "export.noHits";
        public const string ExportNeedSong = "export.needSong";
        public const string ExportFolder = "export.folder";
        public const string ExportFolderApp = "export.folderApp";
        public const string ExportFolderMusic = "export.folderMusic";
        public const string ExportFolderDownloads = "export.folderDownloads";

        // -------------------------------------------------------------- метроном
        public const string MetronomeTitle = "metronome.title";
        public const string MetronomeStart = "metronome.start";
        public const string MetronomeStop = "metronome.stop";
        public const string MetronomeVolume = "metronome.volume";

        // ----------------------------------------------------------- калибровка
        public const string CalibrationTitle = "calibration.title";
        public const string CalibrationStep1 = "calibration.step1";
        public const string CalibrationStep2 = "calibration.step2";
        public const string CalibrationStart = "calibration.start";
        public const string CalibrationSave = "calibration.save";
        public const string CalibrationSkip = "calibration.skip";
        public const string CalibrationResult = "calibration.result";
        public const string CalibrationResultReach = "calibration.resultReach";
        public const string CalibrationMeasuringHeight = "calibration.measuringHeight";
        public const string CalibrationMeasuringReach = "calibration.measuringReach";
        public const string CalibrationDone = "calibration.done";
        public const string CalibrationFailed = "calibration.failed";
        public const string CalibrationSkipped = "calibration.skipped";

        // ---------------------------------------------------------- предпросмотр
        public const string PreviewTitle = "preview.title";
        public const string PreviewSpeed = "preview.speed";
        public const string PreviewSnap = "preview.snap";
        public const string PreviewDivision = "preview.division";
        public const string PreviewNotes = "preview.notes";
        public const string PreviewQuantize = "preview.quantize";
        public const string PreviewSave = "preview.save";

        // ------------------------------------------------------------- служебное
        public const string CommonHits = "common.hits";
        public const string CommonNotes = "common.notes";
        public const string CommonUnknown = "common.unknown";
        public const string CommonLoading = "common.loading";
        public const string CommonOk = "common.ok";
        public const string CommonCancel = "common.cancel";
        public const string CommonYes = "common.yes";
        public const string CommonNo = "common.no";
    }
}
