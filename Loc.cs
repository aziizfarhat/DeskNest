using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeskNest
{
    // Textes de l'application dans 10 langues (anglais par défaut).
    public static class Loc
    {
        // Ordre des traductions : en, fr, it, de, es, pt, ar, zh, ja, ko
        public static readonly (string Code, string Name)[] Languages =
        {
            ("en", "English"), ("fr", "Français"), ("it", "Italiano"), ("de", "Deutsch"), ("es", "Español"),
            ("pt", "Português"), ("ar", "العربية"), ("zh", "中文 (简体)"), ("ja", "日本語"), ("ko", "한국어")
        };

        private static int _idx;
        public static string Current => Languages[_idx].Code;
        public static bool IsRtl => Current == "ar";
        public static event Action? Changed;

        public static System.Windows.MessageBoxOptions MsgOptions => IsRtl
            ? System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign
            : System.Windows.MessageBoxOptions.None;
        public static System.Windows.FlowDirection Flow => IsRtl
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;

        public static void SetLanguage(string? code)
        {
            var resolved = ResolveLanguage(code);
            int i = Array.FindIndex(Languages, l => string.Equals(l.Code, resolved, StringComparison.OrdinalIgnoreCase));
            if (i < 0) i = 0;
            if (i == _idx) return;
            _idx = i;
            Changed?.Invoke();
        }

        // Langue à utiliser : code explicite s'il est pris en charge, sinon langue
        // de l'interface Windows, sinon anglais. Un code vide signifie « automatique ».
        public static string ResolveLanguage(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                int i = IndexOf(code);
                return i >= 0 ? Languages[i].Code : "en";
            }

            foreach (var culture in new[] { CultureInfo.CurrentUICulture, CultureInfo.InstalledUICulture, CultureInfo.CurrentCulture })
            {
                if (culture == null) continue;
                int i = IndexOf(culture.TwoLetterISOLanguageName);
                if (i >= 0) return Languages[i].Code;
            }
            return "en";
        }

        private static int IndexOf(string? code) =>
            string.IsNullOrWhiteSpace(code)
                ? -1
                : Array.FindIndex(Languages, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

        public static string T(string key)
        {
            if (!Table.TryGetValue(key, out var a)) return key;
            var s = a[_idx];
            return string.IsNullOrEmpty(s) ? a[0] : s;
        }

        public static string F(string key, params object[] args) => string.Format(T(key), args);

        // Vrai si le titre est un nom de zone par défaut (« Zone 3 », « Bereich 3 »...), dans n'importe quelle langue.
        public static bool IsDefaultZoneTitle(string title)
        {
            if (title == "Nouvelle zone") return true;
            foreach (var fmt in Table["zone.default"].Distinct())
            {
                var pattern = "^" + Regex.Escape(fmt).Replace("\\{0}", "\\d+") + "$";
                if (Regex.IsMatch(title, pattern)) return true;
            }
            return false;
        }

        private static readonly Dictionary<string, string[]> Table = new();
        private static void A(string key, string en, string fr, string it, string de, string es, string pt, string ar, string zh, string ja, string ko)
            => Table[key] = new[] { en, fr, it, de, es, pt, ar, zh, ja, ko };

        static Loc()
        {
            // ---------- Icône de notification ----------
            A("tray.settings", "Settings…", "Paramètres…", "Impostazioni…", "Einstellungen…", "Configuración…", "Configurações…", "الإعدادات…", "设置…", "設定…", "설정…");
            A("tray.newZone", "New zone", "Nouvelle zone", "Nuova zona", "Neuer Bereich", "Nueva zona", "Nova zona", "منطقة جديدة", "新建区域", "新しいゾーン", "새 영역");
            A("tray.newPortal", "New Folder Portal…", "Nouveau Folder Portal…", "Nuovo Folder Portal…", "Neues Folder Portal…", "Nuevo Folder Portal…", "Novo Folder Portal…", "Folder Portal جديد…", "新建文件夹门户…", "新しいフォルダーポータル…", "새 폴더 포털…");
            A("tray.toggle", "Hide / show zones (Ctrl+Alt+F)", "Masquer / afficher les zones (Ctrl+Alt+F)", "Nascondi / mostra le zone (Ctrl+Alt+F)", "Bereiche aus-/einblenden (Strg+Alt+F)", "Ocultar / mostrar zonas (Ctrl+Alt+F)", "Ocultar / mostrar zonas (Ctrl+Alt+F)", "إخفاء / إظهار المناطق (Ctrl+Alt+F)", "隐藏 / 显示区域 (Ctrl+Alt+F)", "ゾーンを非表示 / 表示 (Ctrl+Alt+F)", "영역 숨기기 / 표시 (Ctrl+Alt+F)");
            A("tray.autostart", "Start with Windows", "Lancer avec Windows", "Avvia con Windows", "Mit Windows starten", "Iniciar con Windows", "Iniciar com o Windows", "التشغيل مع Windows", "随 Windows 启动", "Windows と同時に起動", "Windows와 함께 시작");
            A("tray.restoreIcons", "Put all icons back on the desktop", "Remettre toutes les icônes sur le bureau", "Rimetti tutte le icone sul desktop", "Alle Symbole auf den Desktop zurücklegen", "Devolver todos los iconos al escritorio", "Devolver todos os ícones à área de trabalho", "إعادة كل الأيقونات إلى سطح المكتب", "将所有图标放回桌面", "すべてのアイコンをデスクトップに戻す", "모든 아이콘을 바탕 화면으로 되돌리기");
            A("tray.openConfig", "Open configuration folder", "Ouvrir le dossier de configuration", "Apri la cartella di configurazione", "Konfigurationsordner öffnen", "Abrir la carpeta de configuración", "Abrir a pasta de configuração", "فتح مجلد الإعدادات", "打开配置文件夹", "設定フォルダーを開く", "구성 폴더 열기");
            A("tray.quit", "Quit", "Quitter", "Esci", "Beenden", "Salir", "Sair", "إنهاء", "退出", "終了", "종료");
            A("tray.restoreConfirm",
                "All icons that DeskNest removed from the desktop will be put back there (they will also stay in their zones).\n\nContinue?",
                "Toutes les icônes que DeskNest a retirées du bureau y seront remises (elles resteront aussi dans leurs zones).\n\nContinuer ?",
                "Tutte le icone che DeskNest ha tolto dal desktop verranno rimesse al loro posto (resteranno anche nelle loro zone).\n\nContinuare?",
                "Alle Symbole, die DeskNest vom Desktop entfernt hat, werden dorthin zurückgelegt (sie bleiben auch in ihren Bereichen).\n\nFortfahren?",
                "Todos los iconos que DeskNest quitó del escritorio volverán a él (también seguirán en sus zonas).\n\n¿Continuar?",
                "Todos os ícones que o DeskNest removeu da área de trabalho voltarão para ela (também continuarão nas suas zonas).\n\nContinuar?",
                "ستتم إعادة جميع الأيقونات التي أزالها DeskNest من سطح المكتب إليه (وستبقى أيضًا في مناطقها).\n\nمتابعة؟",
                "DeskNest 从桌面移走的所有图标都将放回桌面（它们也会保留在各自的区域中）。\n\n是否继续？",
                "DeskNest がデスクトップから移動したアイコンをすべてデスクトップに戻します（ゾーン内にも残ります）。\n\n続行しますか？",
                "DeskNest가 바탕 화면에서 옮긴 모든 아이콘이 바탕 화면으로 돌아갑니다(각 영역에도 그대로 남습니다).\n\n계속하시겠습니까?");

            // ---------- Menu d'une zone ----------
            A("m.renameZone", "Rename zone", "Renommer la zone", "Rinomina zona", "Bereich umbenennen", "Renombrar zona", "Renomear zona", "إعادة تسمية المنطقة", "重命名区域", "ゾーン名を変更", "영역 이름 바꾸기");
            A("m.selectAll", "Select all", "Tout sélectionner", "Seleziona tutto", "Alles auswählen", "Seleccionar todo", "Selecionar tudo", "تحديد الكل", "全选", "すべて選択", "모두 선택");
            A("paste", "Paste", "Coller", "Incolla", "Einfügen", "Pegar", "Colar", "لصق", "粘贴", "貼り付け", "붙여넣기");
            A("m.linkFolder", "Link to a folder (Folder Portal)…", "Lier à un dossier (Folder Portal)…", "Collega a una cartella (Folder Portal)…", "Mit einem Ordner verknüpfen (Folder Portal)…", "Vincular a una carpeta (Folder Portal)…", "Vincular a uma pasta (Folder Portal)…", "ربط بمجلد (Folder Portal)…", "关联到文件夹（文件夹门户）…", "フォルダーにリンク（フォルダーポータル）…", "폴더에 연결(폴더 포털)…");
            A("m.changeFolder", "Change folder…", "Changer de dossier…", "Cambia cartella…", "Ordner ändern…", "Cambiar de carpeta…", "Alterar pasta…", "تغيير المجلد…", "更改文件夹…", "フォルダーを変更…", "폴더 변경…");
            A("m.openFolder", "Open folder in Explorer", "Ouvrir le dossier dans l'Explorateur", "Apri la cartella in Esplora risorse", "Ordner im Explorer öffnen", "Abrir la carpeta en el Explorador", "Abrir a pasta no Explorador", "فتح المجلد في مستكشف الملفات", "在资源管理器中打开文件夹", "エクスプローラーでフォルダーを開く", "탐색기에서 폴더 열기");
            A("m.detach", "Detach from folder", "Détacher du dossier", "Scollega dalla cartella", "Vom Ordner trennen", "Desvincular de la carpeta", "Desvincular da pasta", "فصل عن المجلد", "取消关联文件夹", "フォルダーとのリンクを解除", "폴더 연결 해제");
            A("m.color", "Color", "Couleur", "Colore", "Farbe", "Color", "Cor", "اللون", "颜色", "色", "색상");
            A("m.opacity", "Opacity", "Opacité", "Opacità", "Deckkraft", "Opacidad", "Opacidade", "الشفافية", "不透明度", "不透明度", "불투명도");
            A("m.iconSize", "Icon size", "Taille des icônes", "Dimensione icone", "Symbolgröße", "Tamaño de iconos", "Tamanho dos ícones", "حجم الأيقونات", "图标大小", "アイコンのサイズ", "아이콘 크기");
            A("m.small", "Small", "Petites", "Piccole", "Klein", "Pequeños", "Pequenos", "صغيرة", "小", "小", "작게");
            A("m.medium", "Medium", "Moyennes", "Medie", "Mittel", "Medianos", "Médios", "متوسطة", "中", "中", "보통");
            A("m.large", "Large", "Grandes", "Grandi", "Groß", "Grandes", "Grandes", "كبيرة", "大", "大", "크게");
            A("m.view", "View", "Affichage", "Visualizzazione", "Ansicht", "Vista", "Visualização", "العرض", "视图", "表示", "보기");
            A("view.grid", "Grid", "Grille", "Griglia", "Raster", "Cuadrícula", "Grade", "شبكة", "网格", "グリッド", "격자");
            A("view.list", "List", "Liste", "Elenco", "Liste", "Lista", "Lista", "قائمة", "列表", "リスト", "목록");
            A("search.hint", "Search items…", "Rechercher un élément…", "Cerca elementi…", "Elemente suchen…", "Buscar elementos…", "Pesquisar itens…", "بحث عن العناصر…", "搜索项目…", "項目を検索…", "항목 검색…");
            A("search.clear", "Clear search", "Effacer la recherche", "Cancella ricerca", "Suche löschen", "Borrar búsqueda", "Limpar pesquisa", "مسح البحث", "清除搜索", "検索をクリア", "검색 지우기");
            A("m.back", "Back (Alt+Left)", "Retour (Alt+←)", "Indietro (Alt+←)", "Zurück (Alt+←)", "Atrás (Alt+←)", "Voltar (Alt+←)", "رجوع (Alt+←)", "返回 (Alt+←)", "戻る (Alt+←)", "뒤로 (Alt+←)");
            A("m.fonts", "Fonts…", "Polices…", "Caratteri…", "Schriftarten…", "Fuentes…", "Fontes…", "الخطوط…", "字体…", "フォント…", "글꼴…");
            A("m.hideTitle", "Hide title", "Masquer le titre", "Nascondi titolo", "Titel ausblenden", "Ocultar título", "Ocultar título", "إخفاء العنوان", "隐藏标题", "タイトルを非表示", "제목 숨기기");
            A("m.showTitle", "Show title", "Afficher le titre", "Mostra titolo", "Titel einblenden", "Mostrar título", "Mostrar título", "إظهار العنوان", "显示标题", "タイトルを表示", "제목 표시");
            A("m.rollUp", "Roll up", "Réduire (Roll Up)", "Riduci (Roll Up)", "Einklappen (Roll Up)", "Contraer (Roll Up)", "Recolher (Roll Up)", "طيّ (Roll Up)", "折叠 (Roll Up)", "折りたたむ (Roll Up)", "접기 (Roll Up)");
            A("m.expand", "Expand", "Développer", "Espandi", "Ausklappen", "Expandir", "Expandir", "توسيع", "展开", "展開", "펼치기");
            A("m.keepExpanded", "Keep expanded", "Garder développée", "Mantieni espansa", "Ausgeklappt lassen", "Mantener expandida", "Manter expandida", "إبقاء موسّعة", "保持展开", "展開したままにする", "펼친 상태 유지");
            A("m.lock", "Lock", "Verrouiller", "Blocca", "Sperren", "Bloquear", "Bloquear", "قفل", "锁定", "ロック", "잠금");
            A("m.unlock", "Unlock", "Déverrouiller", "Sblocca", "Entsperren", "Desbloquear", "Desbloquear", "إلغاء القفل", "解锁", "ロック解除", "잠금 해제");
            A("m.hoverOn", "Expand on hover: on", "Développement au survol : activé", "Espansione al passaggio: attiva", "Ausklappen bei Mauskontakt: an", "Expandir al pasar el ratón: activado", "Expandir ao passar o mouse: ativado", "التوسيع عند التمرير: مفعّل", "悬停展开：开", "ホバーで展開：オン", "마우스 오버 시 펼치기: 켬");
            A("m.hoverOff", "Expand on hover: off", "Développement au survol : désactivé", "Espansione al passaggio: disattivata", "Ausklappen bei Mauskontakt: aus", "Expandir al pasar el ratón: desactivado", "Expandir ao passar o mouse: desativado", "التوسيع عند التمرير: معطّل", "悬停展开：关", "ホバーで展開：オフ", "마우스 오버 시 펼치기: 끔");
            A("m.deleteZone", "Delete zone…", "Supprimer la zone…", "Elimina zona…", "Bereich löschen…", "Eliminar zona…", "Excluir zona…", "حذف المنطقة…", "删除区域…", "ゾーンを削除…", "영역 삭제…");
            A("m.tooltip", "Zone menu", "Menu de la zone", "Menu della zona", "Bereichsmenü", "Menú de la zona", "Menu da zona", "قائمة المنطقة", "区域菜单", "ゾーンのメニュー", "영역 메뉴");

            A("c.slate", "Slate", "Ardoise", "Ardesia", "Schiefer", "Pizarra", "Ardósia", "أردوازي", "石板灰", "スレート", "슬레이트");
            A("c.blue", "Blue", "Bleu", "Blu", "Blau", "Azul", "Azul", "أزرق", "蓝色", "ブルー", "파랑");
            A("c.green", "Green", "Vert", "Verde", "Grün", "Verde", "Verde", "أخضر", "绿色", "グリーン", "초록");
            A("c.purple", "Purple", "Violet", "Viola", "Violett", "Morado", "Roxo", "بنفسجي", "紫色", "パープル", "보라");
            A("c.red", "Red", "Rouge", "Rosso", "Rot", "Rojo", "Vermelho", "أحمر", "红色", "レッド", "빨강");
            A("c.gray", "Gray", "Gris", "Grigio", "Grau", "Gris", "Cinza", "رمادي", "灰色", "グレー", "회색");
            A("c.black", "Black", "Noir", "Nero", "Schwarz", "Negro", "Preto", "أسود", "黑色", "ブラック", "검정");
            A("c.brown", "Brown", "Marron", "Marrone", "Braun", "Marrón", "Marrom", "بني", "棕色", "ブラウン", "갈색");
            A("c.teal", "Teal", "Sarcelle", "Verde acqua", "Petrol", "Verde azulado", "Verde-azulado", "أزرق مخضر", "青绿色", "ティール", "청록");

            // ---------- Menu d'un élément ----------
            A("i.open", "Open", "Ouvrir", "Apri", "Öffnen", "Abrir", "Abrir", "فتح", "打开", "開く", "열기");
            A("i.openN", "Open ({0} items)", "Ouvrir ({0} éléments)", "Apri ({0} elementi)", "Öffnen ({0} Elemente)", "Abrir ({0} elementos)", "Abrir ({0} itens)", "فتح ({0} عناصر)", "打开（{0} 项）", "開く（{0} 個）", "열기({0}개 항목)");
            A("i.openWith", "Open with…", "Ouvrir avec…", "Apri con…", "Öffnen mit…", "Abrir con…", "Abrir com…", "فتح باستخدام…", "打开方式…", "プログラムから開く…", "연결 프로그램…");
            A("i.location", "Open file location", "Ouvrir l'emplacement du fichier", "Apri percorso file", "Dateispeicherort öffnen", "Abrir ubicación del archivo", "Abrir local do arquivo", "فتح موقع الملف", "打开文件所在位置", "ファイルの場所を開く", "파일 위치 열기");
            A("i.cut", "Cut", "Couper", "Taglia", "Ausschneiden", "Cortar", "Recortar", "قص", "剪切", "切り取り", "잘라내기");
            A("i.copy", "Copy", "Copier", "Copia", "Kopieren", "Copiar", "Copiar", "نسخ", "复制", "コピー", "복사");
            A("i.copyPath", "Copy as path", "Copier en tant que chemin", "Copia come percorso", "Als Pfad kopieren", "Copiar como ruta de acceso", "Copiar como caminho", "نسخ كمسار", "复制文件地址", "パスのコピー", "경로로 복사");
            A("i.rename", "Rename", "Renommer", "Rinomina", "Umbenennen", "Cambiar nombre", "Renomear", "إعادة التسمية", "重命名", "名前の変更", "이름 바꾸기");
            A("i.delete", "Delete", "Supprimer", "Elimina", "Löschen", "Eliminar", "Excluir", "حذف", "删除", "削除", "삭제");
            A("i.deleteN", "Delete ({0} items)", "Supprimer ({0} éléments)", "Elimina ({0} elementi)", "Löschen ({0} Elemente)", "Eliminar ({0} elementos)", "Excluir ({0} itens)", "حذف ({0} عناصر)", "删除（{0} 项）", "削除（{0} 個）", "삭제({0}개 항목)");
            A("i.remove", "Remove from zone", "Retirer de la zone", "Rimuovi dalla zona", "Aus dem Bereich entfernen", "Quitar de la zona", "Remover da zona", "إزالة من المنطقة", "从区域中移除", "ゾーンから削除", "영역에서 제거");
            A("i.removeN", "Remove from zone ({0} items)", "Retirer de la zone ({0} éléments)", "Rimuovi dalla zona ({0} elementi)", "Aus dem Bereich entfernen ({0} Elemente)", "Quitar de la zona ({0} elementos)", "Remover da zona ({0} itens)", "إزالة من المنطقة ({0} عناصر)", "从区域中移除（{0} 项）", "ゾーンから削除（{0} 個）", "영역에서 제거({0}개 항목)");
            A("i.properties", "Properties", "Propriétés", "Proprietà", "Eigenschaften", "Propiedades", "Propriedades", "خصائص", "属性", "プロパティ", "속성");

            // ---------- Messages ----------
            A("msg.unexpected", "An unexpected error occurred. Details were written to the diagnostic log.\n\n{0}", "Une erreur inattendue s'est produite. Les détails ont été écrits dans le journal de diagnostic.\n\n{0}", "Si è verificato un errore imprevisto. I dettagli sono stati scritti nel registro di diagnostica.\n\n{0}", "Ein unerwarteter Fehler ist aufgetreten. Details wurden im Diagnoseprotokoll gespeichert.\n\n{0}", "Se produjo un error inesperado. Los detalles se escribieron en el registro de diagnóstico.\n\n{0}", "Ocorreu um erro inesperado. Os detalhes foram gravados no log de diagnóstico.\n\n{0}", "حدث خطأ غير متوقع. تمت كتابة التفاصيل في سجل التشخيص.\n\n{0}", "发生意外错误。详细信息已写入诊断日志。\n\n{0}", "予期しないエラーが発生しました。詳細は診断ログに書き込まれました。\n\n{0}", "예기치 않은 오류가 발생했습니다. 자세한 내용은 진단 로그에 기록되었습니다.\n\n{0}");
            A("msg.hotkeyFailed", "The shortcut Ctrl+Alt+F is already used by another application. Toggling zones from the keyboard will not work.", "Le raccourci Ctrl+Alt+F est déjà utilisé par une autre application. Le masquage des zones au clavier ne fonctionnera pas.", "La scorciatoia Ctrl+Alt+F è già utilizzata da un'altra applicazione. L'attivazione/disattivazione delle zone da tastiera non funzionerà.", "Die Tastenkombination Strg+Alt+F wird bereits von einer anderen Anwendung verwendet. Das Ein-/Ausblenden der Bereiche per Tastatur funktioniert nicht.", "La combinación Ctrl+Alt+F ya la utiliza otra aplicación. La ocultación de zonas con el teclado no funcionará.", "O atalho Ctrl+Alt+F já está a ser utilizado por outra aplicação. Alternar as zonas pelo teclado não funcionará.", "الاختصار Ctrl+Alt+F مستخدم بالفعل من قبل تطبيق آخر. لن يعمل إخفاء/إظهار المناطق من لوحة المفاتيح.", "快捷键 Ctrl+Alt+F 已被其他应用程序占用。将无法通过键盘切换区域。", "ショートカット Ctrl+Alt+F は別のアプリで使用されています。キーボードによるゾーンの切り替えは機能しません。", "Ctrl+Alt+F 단축키를 다른 앱에서 이미 사용 중입니다. 키보드로 영역을 숨기거나 표시할 수 없습니다.");
            A("msg.layoutRestored", "The configuration file was unreadable. The most recent backup was restored: {0}", "Le fichier de configuration était illisible. La sauvegarde la plus récente a été restaurée : {0}", "Il file di configurazione non era leggibile. È stato ripristinato il backup più recente: {0}", "Die Konfigurationsdatei war unlesbar. Die neueste Sicherung wurde wiederhergestellt: {0}", "El archivo de configuración no se pudo leer. Se restauró la copia de seguridad más reciente: {0}", "O ficheiro de configuração estava ilegível. O backup mais recente foi restaurado: {0}", "تعذّرت قراءة ملف الإعدادات. تمت استعادة أحدث نسخة احتياطية: {0}", "配置文件无法读取。已恢复最近的备份：{0}", "構成ファイルを読み取れませんでした。最新のバックアップを復元しました: {0}", "구성 파일을 읽을 수 없어 가장 최근 백업을 복원했습니다: {0}");
            A("msg.layoutLost", "The configuration file was unreadable and no valid backup was found. A new empty layout was started; the damaged file was kept in the configuration folder.", "Le fichier de configuration était illisible et aucune sauvegarde valide n'a été trouvée. Une nouvelle disposition vide a été démarrée ; le fichier endommagé a été conservé dans le dossier de configuration.", "Il file di configurazione non era leggibile e non è stato trovato alcun backup valido. È stata avviata una nuova disposizione vuota; il file danneggiato è stato conservato nella cartella di configurazione.", "Die Konfigurationsdatei war unlesbar und es wurde keine gültige Sicherung gefunden. Es wurde ein neues leeres Layout gestartet; die beschädigte Datei wurde im Konfigurationsordner aufbewahrt.", "El archivo de configuración no se pudo leer y no se encontró ninguna copia de seguridad válida. Se inició una nueva disposición vacía; el archivo dañado se conservó en la carpeta de configuración.", "O ficheiro de configuração estava ilegível e não foi encontrado nenhum backup válido. Foi iniciada uma nova disposição vazia; o ficheiro danificado foi mantido na pasta de configuração.", "تعذّرت قراءة ملف الإعدادات ولم يتم العثور على نسخة احتياطية صالحة. تم بدء تخطيط فارغ جديد؛ وتم الاحتفاظ بالملف التالف في مجلد الإعدادات.", "配置文件无法读取，且未找到有效备份。已启动新的空布局；损坏的文件已保留在配置文件夹中。", "構成ファイルを読み取れず、有効なバックアップも見つかりませんでした。新しい空のレイアウトで起動しました。破損したファイルは構成フォルダーに保持されています。", "구성 파일을 읽을 수 없고 유효한 백업도 없어 새 빈 레이아웃으로 시작했습니다. 손상된 파일은 구성 폴더에 보관했습니다.");
            A("msg.cannotOpen", "Unable to open:\n{0}\n\n{1}", "Impossible d'ouvrir :\n{0}\n\n{1}", "Impossibile aprire:\n{0}\n\n{1}", "Öffnen nicht möglich:\n{0}\n\n{1}", "No se puede abrir:\n{0}\n\n{1}", "Não foi possível abrir:\n{0}\n\n{1}", "تعذّر الفتح:\n{0}\n\n{1}", "无法打开：\n{0}\n\n{1}", "開けません:\n{0}\n\n{1}", "열 수 없습니다:\n{0}\n\n{1}");
            A("msg.delete1", "Move “{0}” to the Recycle Bin?", "Déplacer « {0} » vers la Corbeille ?", "Spostare “{0}” nel Cestino?", "„{0}“ in den Papierkorb verschieben?", "¿Mover “{0}” a la Papelera de reciclaje?", "Mover “{0}” para a Lixeira?", "نقل “{0}” إلى سلة المحذوفات؟", "将“{0}”移到回收站？", "「{0}」をごみ箱に移動しますか？", "“{0}”을(를) 휴지통으로 이동하시겠습니까?");
            A("msg.deleteN", "Move these {0} items to the Recycle Bin?", "Déplacer ces {0} éléments vers la Corbeille ?", "Spostare questi {0} elementi nel Cestino?", "Diese {0} Elemente in den Papierkorb verschieben?", "¿Mover estos {0} elementos a la Papelera de reciclaje?", "Mover estes {0} itens para a Lixeira?", "نقل هذه العناصر ({0}) إلى سلة المحذوفات؟", "将这 {0} 项移到回收站？", "これら {0} 個の項目をごみ箱に移動しますか？", "이 {0}개 항목을 휴지통으로 이동하시겠습니까?");
            A("msg.deleteFailed", "Could not delete “{0}”: {1}", "Impossible de supprimer « {0} » : {1}", "Impossibile eliminare “{0}”: {1}", "„{0}“ konnte nicht gelöscht werden: {1}", "No se pudo eliminar “{0}”: {1}", "Não foi possível excluir “{0}”: {1}", "تعذّر حذف “{0}”: {1}", "无法删除“{0}”：{1}", "「{0}」を削除できませんでした: {1}", "“{0}”을(를) 삭제할 수 없습니다: {1}");
            A("msg.renameInvalid",
                "A file name cannot contain any of the following characters:\n\\ / : * ? \" < > |",
                "Un nom de fichier ne peut contenir aucun des caractères suivants :\n\\ / : * ? \" < > |",
                "Un nome di file non può contenere nessuno dei seguenti caratteri:\n\\ / : * ? \" < > |",
                "Ein Dateiname darf keines der folgenden Zeichen enthalten:\n\\ / : * ? \" < > |",
                "Un nombre de archivo no puede contener ninguno de los siguientes caracteres:\n\\ / : * ? \" < > |",
                "Um nome de arquivo não pode conter nenhum dos seguintes caracteres:\n\\ / : * ? \" < > |",
                "لا يمكن أن يحتوي اسم الملف على أي من الأحرف التالية:\n\\ / : * ? \" < > |",
                "文件名不能包含下列任何字符：\n\\ / : * ? \" < > |",
                "ファイル名には次の文字は使えません:\n\\ / : * ? \" < > |",
                "파일 이름에는 다음 문자를 사용할 수 없습니다:\n\\ / : * ? \" < > |");
            A("msg.renameExists", "An item with this name already exists.", "Un élément portant ce nom existe déjà.", "Esiste già un elemento con questo nome.", "Ein Element mit diesem Namen existiert bereits.", "Ya existe un elemento con este nombre.", "Já existe um item com este nome.", "يوجد عنصر بهذا الاسم بالفعل.", "已存在同名项目。", "同じ名前の項目が既に存在します。", "같은 이름의 항목이 이미 있습니다.");
            A("msg.renameFailed", "Could not rename: {0}", "Impossible de renommer : {0}", "Impossibile rinominare: {0}", "Umbenennen nicht möglich: {0}", "No se pudo cambiar el nombre: {0}", "Não foi possível renomear: {0}", "تعذّرت إعادة التسمية: {0}", "无法重命名：{0}", "名前を変更できません: {0}", "이름을 바꿀 수 없습니다: {0}");
            A("msg.portalReplace",
                "The current items of this zone will be removed (files are not deleted; those that came from the desktop go back there).\n\nContinue?",
                "Les éléments actuels de cette zone seront retirés (les fichiers ne sont pas supprimés ; ceux qui venaient du bureau y retournent).\n\nContinuer ?",
                "Gli elementi attuali di questa zona verranno rimossi (i file non vengono eliminati; quelli provenienti dal desktop vi ritornano).\n\nContinuare?",
                "Die aktuellen Elemente dieses Bereichs werden entfernt (Dateien werden nicht gelöscht; solche vom Desktop gehen dorthin zurück).\n\nFortfahren?",
                "Los elementos actuales de esta zona se quitarán (los archivos no se eliminan; los que venían del escritorio vuelven a él).\n\n¿Continuar?",
                "Os itens atuais desta zona serão removidos (os arquivos não são excluídos; os que vieram da área de trabalho voltam para ela).\n\nContinuar?",
                "ستتم إزالة العناصر الحالية من هذه المنطقة (لا تُحذف الملفات؛ وتعود التي جاءت من سطح المكتب إليه).\n\nمتابعة؟",
                "此区域中的当前项目将被移除（不会删除文件；来自桌面的项目会回到桌面）。\n\n是否继续？",
                "このゾーンの現在の項目は削除されます（ファイルは削除されません。デスクトップから来た項目はデスクトップに戻ります）。\n\n続行しますか？",
                "이 영역의 현재 항목이 제거됩니다(파일은 삭제되지 않으며, 바탕 화면에서 온 항목은 바탕 화면으로 돌아갑니다).\n\n계속하시겠습니까?");
            A("msg.deleteZone",
                "Delete the zone “{0}”?\n\nThe files it references are not deleted (those that came from the desktop go back there).",
                "Supprimer la zone « {0} » ?\n\nLes fichiers qu'elle référence ne seront pas supprimés (ceux qui venaient du bureau y retournent).",
                "Eliminare la zona “{0}”?\n\nI file a cui fa riferimento non vengono eliminati (quelli provenienti dal desktop vi ritornano).",
                "Bereich „{0}“ löschen?\n\nDie referenzierten Dateien werden nicht gelöscht (solche vom Desktop gehen dorthin zurück).",
                "¿Eliminar la zona “{0}”?\n\nLos archivos a los que hace referencia no se eliminan (los que venían del escritorio vuelven a él).",
                "Excluir a zona “{0}”?\n\nOs arquivos referenciados não são excluídos (os que vieram da área de trabalho voltam para ela).",
                "حذف المنطقة “{0}”؟\n\nلا يتم حذف الملفات المشار إليها (وتعود التي جاءت من سطح المكتب إليه).",
                "删除区域“{0}”？\n\n它引用的文件不会被删除（来自桌面的项目会回到桌面）。",
                "ゾーン「{0}」を削除しますか？\n\n参照しているファイルは削除されません（デスクトップから来た項目はデスクトップに戻ります）。",
                "“{0}” 영역을 삭제하시겠습니까?\n\n참조하는 파일은 삭제되지 않습니다(바탕 화면에서 온 항목은 바탕 화면으로 돌아갑니다).");
            A("msg.folderNotFound", "The destination folder could not be found.", "Le dossier de destination est introuvable.", "Impossibile trovare la cartella di destinazione.", "Der Zielordner wurde nicht gefunden.", "No se encontró la carpeta de destino.", "A pasta de destino não foi encontrada.", "تعذّر العثور على المجلد الوجهة.", "找不到目标文件夹。", "移動先のフォルダーが見つかりません。", "대상 폴더를 찾을 수 없습니다.");
            A("msg.folderIntoItself", "A folder cannot be moved into itself.", "Impossible de déplacer un dossier dans lui-même.", "Impossibile spostare una cartella in se stessa.", "Ein Ordner kann nicht in sich selbst verschoben werden.", "No se puede mover una carpeta dentro de sí misma.", "Não é possível mover uma pasta para dentro dela mesma.", "لا يمكن نقل مجلد إلى داخل نفسه.", "无法将文件夹移动到它自身中。", "フォルダーを自分自身の中に移動できません。", "폴더를 자기 자신 안으로 이동할 수 없습니다.");
            A("msg.opFailed", "Operation failed on “{0}”: {1}", "Opération impossible sur « {0} » : {1}", "Operazione non riuscita su “{0}”: {1}", "Vorgang für „{0}“ fehlgeschlagen: {1}", "Error en la operación con “{0}”: {1}", "Falha na operação com “{0}”: {1}", "فشلت العملية على “{0}”: {1}", "对“{0}”的操作失败：{1}", "「{0}」の操作に失敗しました: {1}", "“{0}” 작업 실패: {1}");
            A("msg.publicDesktop",
                "“{0}” is on the Public Desktop (shared by all users). Windows requires administrator permission to take it off the desktop.\n\nAllow?",
                "« {0} » se trouve sur le Bureau public (partagé entre tous les utilisateurs). Windows demande une autorisation administrateur pour le retirer du bureau.\n\nAutoriser ?",
                "“{0}” si trova sul Desktop pubblico (condiviso da tutti gli utenti). Windows richiede l'autorizzazione di amministratore per rimuoverlo dal desktop.\n\nConsentire?",
                "„{0}“ befindet sich auf dem öffentlichen Desktop (für alle Benutzer). Windows benötigt Administratorrechte, um es vom Desktop zu entfernen.\n\nZulassen?",
                "“{0}” está en el Escritorio público (compartido por todos los usuarios). Windows requiere permiso de administrador para quitarlo del escritorio.\n\n¿Permitir?",
                "“{0}” está na Área de Trabalho pública (compartilhada por todos os usuários). O Windows exige permissão de administrador para removê-lo da área de trabalho.\n\nPermitir?",
                "“{0}” موجود على سطح المكتب العام (مشترك بين كل المستخدمين). يتطلب Windows إذن المسؤول لإزالته من سطح المكتب.\n\nالسماح؟",
                "“{0}”位于公共桌面（所有用户共享）。Windows 需要管理员权限才能将其从桌面移除。\n\n是否允许？",
                "「{0}」はパブリック デスクトップ（全ユーザーで共有）にあります。デスクトップから取り除くには、Windows の管理者の許可が必要です。\n\n許可しますか？",
                "“{0}”은(는) 공용 바탕 화면(모든 사용자 공유)에 있습니다. 바탕 화면에서 제거하려면 Windows 관리자 권한이 필요합니다.\n\n허용하시겠습니까?");
            A("msg.publicDesktopBack",
                "This item must go back to the Public Desktop: Windows requires administrator permission.\n\nAllow?",
                "Cet élément doit retourner sur le Bureau public : Windows demande une autorisation administrateur.\n\nAutoriser ?",
                "Questo elemento deve tornare sul Desktop pubblico: Windows richiede l'autorizzazione di amministratore.\n\nConsentire?",
                "Dieses Element muss auf den öffentlichen Desktop zurück: Windows benötigt Administratorrechte.\n\nZulassen?",
                "Este elemento debe volver al Escritorio público: Windows requiere permiso de administrador.\n\n¿Permitir?",
                "Este item deve voltar à Área de Trabalho pública: o Windows exige permissão de administrador.\n\nPermitir?",
                "يجب أن يعود هذا العنصر إلى سطح المكتب العام: يتطلب Windows إذن المسؤول.\n\nالسماح؟",
                "此项目需要放回公共桌面：Windows 需要管理员权限。\n\n是否允许？",
                "この項目をパブリック デスクトップに戻すには、Windows の管理者の許可が必要です。\n\n許可しますか？",
                "이 항목을 공용 바탕 화면으로 되돌리려면 Windows 관리자 권한이 필요합니다.\n\n허용하시겠습니까?");
            A("msg.cannotRemove",
                "Unable to remove “{0}” from the desktop (insufficient rights or item in use). It stays visible on the desktop but has been added to the zone.",
                "Impossible de retirer « {0} » du bureau (droits insuffisants ou élément en cours d'utilisation). Il reste visible sur le bureau, mais il est bien ajouté à la zone.",
                "Impossibile rimuovere “{0}” dal desktop (permessi insufficienti o elemento in uso). Resta visibile sul desktop, ma è stato aggiunto alla zona.",
                "„{0}“ konnte nicht vom Desktop entfernt werden (fehlende Rechte oder Element in Verwendung). Es bleibt auf dem Desktop sichtbar, wurde aber dem Bereich hinzugefügt.",
                "No se pudo quitar “{0}” del escritorio (permisos insuficientes o elemento en uso). Sigue visible en el escritorio, pero se ha añadido a la zona.",
                "Não foi possível remover “{0}” da área de trabalho (direitos insuficientes ou item em uso). Ele continua visível na área de trabalho, mas foi adicionado à zona.",
                "تعذّرت إزالة “{0}” من سطح المكتب (صلاحيات غير كافية أو العنصر قيد الاستخدام). يبقى ظاهرًا على سطح المكتب لكنه أُضيف إلى المنطقة.",
                "无法将“{0}”从桌面移除（权限不足或项目正在使用）。它仍显示在桌面上，但已添加到区域中。",
                "「{0}」をデスクトップから取り除けません（権限不足、または使用中）。デスクトップには表示されたままですが、ゾーンには追加されました。",
                "“{0}”을(를) 바탕 화면에서 제거할 수 없습니다(권한 부족 또는 사용 중). 바탕 화면에는 계속 표시되지만 영역에는 추가되었습니다.");
            A("msg.protectedWarn",
                "Windows is set to show protected operating system files: hidden icons will stay visible (semi-transparent) on the desktop.\n\nTo hide them completely: File Explorer > Folder Options > View > check “Hide protected operating system files”. “Move” mode avoids this setting.",
                "Windows est configuré pour afficher les fichiers protégés du système : les icônes masquées resteront visibles (semi-transparentes) sur le bureau.\n\nPour les cacher complètement : Explorateur > Options des dossiers > Affichage > cocher « Masquer les fichiers protégés du système d'exploitation ». Le mode « Déplacer » évite ce réglage.",
                "Windows è configurato per mostrare i file protetti del sistema operativo: le icone nascoste resteranno visibili (semitrasparenti) sul desktop.\n\nPer nasconderle completamente: Esplora file > Opzioni cartelle > Visualizzazione > seleziona «Nascondi i file protetti del sistema operativo». La modalità «Sposta» evita questa impostazione.",
                "Windows ist so eingestellt, dass geschützte Systemdateien angezeigt werden: Ausgeblendete Symbole bleiben (halbtransparent) auf dem Desktop sichtbar.\n\nUm sie vollständig auszublenden: Explorer > Ordneroptionen > Ansicht > „Geschützte Systemdateien ausblenden“ aktivieren. Der Modus „Verschieben“ umgeht diese Einstellung.",
                "Windows está configurado para mostrar los archivos protegidos del sistema operativo: los iconos ocultos seguirán visibles (semitransparentes) en el escritorio.\n\nPara ocultarlos por completo: Explorador > Opciones de carpeta > Vista > marque «Ocultar archivos protegidos del sistema operativo». El modo «Mover» evita este ajuste.",
                "O Windows está configurado para mostrar arquivos protegidos do sistema operacional: os ícones ocultos continuarão visíveis (semitransparentes) na área de trabalho.\n\nPara ocultá-los totalmente: Explorador > Opções de Pasta > Exibição > marque «Ocultar arquivos protegidos do sistema operacional». O modo «Mover» evita essa configuração.",
                "تم ضبط Windows لإظهار ملفات النظام المحمية: ستبقى الأيقونات المخفية ظاهرة (شبه شفافة) على سطح المكتب.\n\nلإخفائها تمامًا: مستكشف الملفات > خيارات المجلد > عرض > حدّد «إخفاء ملفات نظام التشغيل المحمية». يتجاوز وضع «نقل» هذا الإعداد.",
                "Windows 已设置为显示受保护的操作系统文件：被隐藏的图标仍会以半透明形式显示在桌面上。\n\n要完全隐藏：文件资源管理器 > 文件夹选项 > 查看 > 勾选“隐藏受保护的操作系统文件”。使用“移动”模式可避免此设置。",
                "Windows が保護されたオペレーティング システム ファイルを表示する設定になっているため、非表示にしたアイコンもデスクトップに（半透明で）表示されたままになります。\n\n完全に隠すには: エクスプローラー > フォルダー オプション > 表示 > 「保護されたオペレーティング システム ファイルを表示しない」をオンにしてください。「移動」モードならこの設定の影響を受けません。",
                "Windows가 보호된 운영 체제 파일을 표시하도록 설정되어 있어 숨긴 아이콘이 바탕 화면에 반투명하게 계속 표시됩니다.\n\n완전히 숨기려면: 파일 탐색기 > 폴더 옵션 > 보기 > ‘보호된 운영 체제 파일 숨기기’를 선택하세요. ‘이동’ 모드는 이 설정의 영향을 받지 않습니다.");
            A("msg.cannotRestore",
                "Unable to put the item back on the desktop. It remains in:\n{0}",
                "Impossible de remettre l'élément sur le bureau. Il reste dans :\n{0}",
                "Impossibile rimettere l'elemento sul desktop. Rimane in:\n{0}",
                "Das Element konnte nicht auf den Desktop zurückgelegt werden. Es bleibt in:\n{0}",
                "No se pudo devolver el elemento al escritorio. Permanece en:\n{0}",
                "Não foi possível devolver o item à área de trabalho. Ele permanece em:\n{0}",
                "تعذّرت إعادة العنصر إلى سطح المكتب. يبقى في:\n{0}",
                "无法将项目放回桌面。它仍位于：\n{0}",
                "項目をデスクトップに戻せませんでした。次の場所に残っています:\n{0}",
                "항목을 바탕 화면으로 되돌릴 수 없습니다. 다음 위치에 남아 있습니다:\n{0}");

            A("hint.moveInto", "Move into “{0}”", "Déplacer dans « {0} »", "Sposta in “{0}”", "Verschieben nach „{0}“", "Mover a “{0}”", "Mover para “{0}”", "نقل إلى “{0}”", "移动到“{0}”", "「{0}」に移動", "“{0}”(으)로 이동");
            A("hint.copyInto", "Copy into “{0}”", "Copier dans « {0} »", "Copia in “{0}”", "Kopieren nach „{0}“", "Copiar en “{0}”", "Copiar para “{0}”", "نسخ إلى “{0}”", "复制到“{0}”", "「{0}」にコピー", "“{0}”(으)로 복사");
            A("hint.parent", "parent folder", "dossier parent", "cartella superiore", "übergeordneter Ordner", "carpeta superior", "pasta superior", "المجلد الأصل", "上级文件夹", "親フォルダー", "상위 폴더");
            A("portal.page", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}");
            A("dlg.chooseFolder", "Choose the folder to display in this zone", "Choisir le dossier à afficher dans cette zone", "Scegli la cartella da mostrare in questa zona", "Ordner für diesen Bereich auswählen", "Elija la carpeta que se mostrará en esta zona", "Escolha a pasta a exibir nesta zona", "اختر المجلد المراد عرضه في هذه المنطقة", "选择要在此区域中显示的文件夹", "このゾーンに表示するフォルダーを選択", "이 영역에 표시할 폴더 선택");
            A("dlg.ok", "OK", "OK", "OK", "OK", "Aceptar", "OK", "موافق", "确定", "OK", "확인");
            A("dlg.cancel", "Cancel", "Annuler", "Annulla", "Abbrechen", "Cancelar", "Cancelar", "إلغاء", "取消", "キャンセル", "취소");
            A("zone.default", "Zone {0}", "Zone {0}", "Zona {0}", "Bereich {0}", "Zona {0}", "Zona {0}", "المنطقة {0}", "区域 {0}", "ゾーン {0}", "영역 {0}");
            A("z.hidden", "(hidden)", "(masquée)", "(nascosta)", "(ausgeblendet)", "(oculta)", "(oculta)", "(مخفية)", "（已隐藏）", "（非表示）", "(숨김)");
            A("zone.apps", "Applications", "Applications", "Applicazioni", "Anwendungen", "Aplicaciones", "Aplicativos", "التطبيقات", "应用程序", "アプリ", "앱");

            // ---------- Fenêtre de paramètres ----------
            A("s.title", "DeskNest — Settings", "DeskNest — Paramètres", "DeskNest — Impostazioni", "DeskNest — Einstellungen", "DeskNest — Configuración", "DeskNest — Configurações", "DeskNest — الإعدادات", "DeskNest — 设置", "DeskNest — 設定", "DeskNest — 설정");
            A("tab.general", "General", "Général", "Generale", "Allgemein", "General", "Geral", "عام", "常规", "一般", "일반");
            A("tab.zones", "Zones", "Zones", "Zone", "Bereiche", "Zonas", "Zonas", "المناطق", "区域", "ゾーン", "영역");
            A("tab.behavior", "Behavior", "Comportement", "Comportamento", "Verhalten", "Comportamiento", "Comportamento", "السلوك", "行为", "動作", "동작");
            A("tab.appearance", "Appearance", "Apparence", "Aspetto", "Darstellung", "Apariencia", "Aparência", "المظهر", "外观", "外観", "모양");
            A("tab.backups", "Backups", "Sauvegardes", "Backup", "Sicherungen", "Copias de seguridad", "Backups", "النسخ الاحتياطي", "备份", "バックアップ", "백업");
            A("tab.about", "About", "À propos", "Informazioni", "Info", "Acerca de", "Sobre", "حول", "关于", "バージョン情報", "정보");
            A("s.close", "Close", "Fermer", "Chiudi", "Schließen", "Cerrar", "Fechar", "إغلاق", "关闭", "閉じる", "닫기");

            A("g.enabled", "Enable DeskNest (show zones on the desktop)", "Activer DeskNest (afficher les zones sur le bureau)", "Attiva DeskNest (mostra le zone sul desktop)", "DeskNest aktivieren (Bereiche auf dem Desktop anzeigen)", "Activar DeskNest (mostrar las zonas en el escritorio)", "Ativar o DeskNest (mostrar as zonas na área de trabalho)", "تفعيل DeskNest (إظهار المناطق على سطح المكتب)", "启用 DeskNest（在桌面上显示区域）", "DeskNest を有効にする（デスクトップにゾーンを表示）", "DeskNest 사용(바탕 화면에 영역 표시)");
            A("g.autostart", "Start DeskNest with Windows", "Lancer DeskNest au démarrage de Windows", "Avvia DeskNest con Windows", "DeskNest mit Windows starten", "Iniciar DeskNest con Windows", "Iniciar o DeskNest com o Windows", "تشغيل DeskNest عند بدء Windows", "随 Windows 启动 DeskNest", "Windows の起動時に DeskNest を起動する", "Windows 시작 시 DeskNest 실행");
            A("g.hotkey", "Keyboard shortcut Ctrl + Alt + F: hide / show zones", "Raccourci clavier Ctrl + Alt + F : masquer / afficher les zones", "Scorciatoia Ctrl + Alt + F: nascondi / mostra le zone", "Tastenkombination Strg + Alt + F: Bereiche aus-/einblenden", "Atajo de teclado Ctrl + Alt + F: ocultar / mostrar zonas", "Atalho de teclado Ctrl + Alt + F: ocultar / mostrar zonas", "اختصار لوحة المفاتيح Ctrl + Alt + F: إخفاء / إظهار المناطق", "快捷键 Ctrl + Alt + F：隐藏 / 显示区域", "ショートカット Ctrl + Alt + F: ゾーンの表示 / 非表示", "단축키 Ctrl + Alt + F: 영역 숨기기 / 표시");
            A("g.dblclick", "Double-click an empty area of the desktop: hide / show zones", "Double-clic sur une zone vide du bureau : masquer / afficher les zones", "Doppio clic su un'area vuota del desktop: nascondi / mostra le zone", "Doppelklick auf eine leere Desktopfläche: Bereiche aus-/einblenden", "Doble clic en un área vacía del escritorio: ocultar / mostrar zonas", "Clique duplo numa área vazia da área de trabalho: ocultar / mostrar zonas", "نقر مزدوج على منطقة فارغة من سطح المكتب: إخفاء / إظهار المناطق", "双击桌面空白处：隐藏 / 显示区域", "デスクトップの空白をダブルクリック: ゾーンの表示 / 非表示", "바탕 화면 빈 곳 더블 클릭: 영역 숨기기 / 표시");
            A("g.hideIcons", "Remove from the desktop the icons dragged into a zone", "Retirer du bureau les icônes glissées dans une zone", "Rimuovi dal desktop le icone trascinate in una zona", "Symbole, die in einen Bereich gezogen werden, vom Desktop entfernen", "Quitar del escritorio los iconos arrastrados a una zona", "Remover da área de trabalho os ícones arrastados para uma zona", "إزالة الأيقونات المسحوبة إلى منطقة من سطح المكتب", "将拖入区域的图标从桌面移除", "ゾーンにドラッグしたアイコンをデスクトップから取り除く", "영역으로 끌어온 아이콘을 바탕 화면에서 제거");
            A("g.searchBar", "Show a search bar in each zone", "Afficher une barre de recherche dans chaque zone", "Mostra una barra di ricerca in ogni zona", "Suchleiste in jedem Bereich anzeigen", "Mostrar una barra de búsqueda en cada zona", "Mostrar uma barra de pesquisa em cada zona", "إظهار شريط بحث في كل منطقة", "在每个区域中显示搜索栏", "各ゾーンに検索バーを表示", "각 영역에 검색 표시줄 표시");
            A("g.method", "Method used to remove icons from the desktop", "Méthode pour faire disparaître les icônes du bureau", "Metodo per rimuovere le icone dal desktop", "Methode zum Entfernen der Symbole vom Desktop", "Método para quitar los iconos del escritorio", "Método para remover os ícones da área de trabalho", "طريقة إزالة الأيقونات من سطح المكتب", "从桌面移除图标的方式", "デスクトップからアイコンを取り除く方法", "바탕 화면에서 아이콘을 제거하는 방법");
            A("g.modeMove", "Move the item into a folder managed by DeskNest (recommended)", "Déplacer l'élément dans un dossier géré par DeskNest (recommandé)", "Sposta l'elemento in una cartella gestita da DeskNest (consigliato)", "Element in einen von DeskNest verwalteten Ordner verschieben (empfohlen)", "Mover el elemento a una carpeta gestionada por DeskNest (recomendado)", "Mover o item para uma pasta gerenciada pelo DeskNest (recomendado)", "نقل العنصر إلى مجلد يديره DeskNest (موصى به)", "将项目移到 DeskNest 管理的文件夹（推荐）", "項目を DeskNest が管理するフォルダーに移動（推奨）", "항목을 DeskNest가 관리하는 폴더로 이동(권장)");
            A("g.modeHide", "Only hide it on the desktop (hidden + system attributes)", "Seulement le cacher sur le bureau (attribut caché + système)", "Nascondilo soltanto sul desktop (attributi nascosto + sistema)", "Nur auf dem Desktop ausblenden (Attribute versteckt + System)", "Solo ocultarlo en el escritorio (atributos oculto + sistema)", "Apenas ocultá-lo na área de trabalho (atributos oculto + sistema)", "إخفاؤه فقط على سطح المكتب (سمتا مخفي + نظام)", "仅在桌面上隐藏（隐藏 + 系统属性）", "デスクトップ上で非表示にするだけ（隠し + システム属性）", "바탕 화면에서 숨기기만 함(숨김 + 시스템 특성)");
            A("g.hint",
                "“Move” mode: the item is stored in the “DeskNest\\Items” folder of your profile and put back in its original place when you remove it from the zone or delete the zone. For Public Desktop icons, Windows asks for administrator permission. Nothing is ever deleted.",
                "Mode « Déplacer » : l'élément est rangé dans le dossier « DeskNest\\Items » de votre profil et remis à sa place d'origine quand vous le retirez de la zone ou supprimez la zone. Pour les icônes du Bureau public, Windows demande une autorisation administrateur. Rien n'est jamais supprimé.",
                "Modalità «Sposta»: l'elemento viene riposto nella cartella «DeskNest\\Items» del tuo profilo e rimesso al suo posto originale quando lo rimuovi dalla zona o elimini la zona. Per le icone del Desktop pubblico, Windows richiede l'autorizzazione di amministratore. Non viene mai eliminato nulla.",
                "Modus „Verschieben“: Das Element wird im Ordner „DeskNest\\Items“ Ihres Profils abgelegt und an seinen ursprünglichen Ort zurückgelegt, wenn Sie es aus dem Bereich entfernen oder den Bereich löschen. Für Symbole des öffentlichen Desktops verlangt Windows Administratorrechte. Es wird nie etwas gelöscht.",
                "Modo «Mover»: el elemento se guarda en la carpeta «DeskNest\\Items» de su perfil y vuelve a su lugar original cuando lo quita de la zona o elimina la zona. Para los iconos del Escritorio público, Windows pide permiso de administrador. Nunca se elimina nada.",
                "Modo «Mover»: o item é guardado na pasta «DeskNest\\Items» do seu perfil e volta ao lugar original quando você o remove da zona ou exclui a zona. Para os ícones da Área de Trabalho pública, o Windows pede permissão de administrador. Nada é excluído.",
                "وضع «نقل»: يُحفظ العنصر في المجلد «DeskNest\\Items» ضمن ملفك الشخصي ويعود إلى مكانه الأصلي عند إزالته من المنطقة أو حذف المنطقة. بالنسبة لأيقونات سطح المكتب العام، يطلب Windows إذن المسؤول. لا يتم حذف أي شيء.",
                "“移动”模式：项目会存放在你的用户配置文件下的“DeskNest\\Items”文件夹中，从区域移除项目或删除区域时，它会回到原来的位置。对于公共桌面的图标，Windows 会要求管理员权限。不会删除任何内容。",
                "「移動」モード: 項目はプロファイル内の「DeskNest\\Items」フォルダーに保管され、ゾーンから削除したりゾーンを削除したりすると元の場所に戻ります。パブリック デスクトップのアイコンでは、Windows が管理者の許可を求めます。何も削除されません。",
                "“이동” 모드: 항목은 사용자 프로필의 “DeskNest\\Items” 폴더에 보관되며, 영역에서 제거하거나 영역을 삭제하면 원래 위치로 돌아갑니다. 공용 바탕 화면 아이콘은 Windows가 관리자 권한을 요청합니다. 아무것도 삭제되지 않습니다.");
            A("g.restoreResult", "{0} icon(s) put back on the desktop.", "{0} icône(s) remise(s) sur le bureau.", "{0} icona/e rimessa/e sul desktop.", "{0} Symbol(e) auf den Desktop zurückgelegt.", "{0} icono(s) devuelto(s) al escritorio.", "{0} ícone(s) devolvido(s) à área de trabalho.", "تمت إعادة {0} أيقونة إلى سطح المكتب.", "已将 {0} 个图标放回桌面。", "{0} 個のアイコンをデスクトップに戻しました。", "아이콘 {0}개를 바탕 화면으로 되돌렸습니다.");
            A("g.language", "Language", "Langue", "Lingua", "Sprache", "Idioma", "Idioma", "اللغة", "语言", "言語", "언어");

            A("z.visible", "Zone visible", "Zone visible", "Zona visibile", "Bereich sichtbar", "Zona visible", "Zona visível", "المنطقة ظاهرة", "区域可见", "ゾーンを表示", "영역 표시");
            A("z.name", "Name", "Nom", "Nome", "Name", "Nombre", "Nome", "الاسم", "名称", "名前", "이름");
            A("z.view", "Item display", "Affichage des éléments", "Visualizzazione elementi", "Elementanzeige", "Visualización de elementos", "Exibição dos itens", "عرض العناصر", "项目显示", "項目の表示", "항목 표시");
            A("z.hex", "or color code (e.g. #1E1E2E)", "ou code couleur (ex. #1E1E2E)", "oppure codice colore (es. #1E1E2E)", "oder Farbcode (z. B. #1E1E2E)", "o código de color (p. ej. #1E1E2E)", "ou código de cor (ex.: #1E1E2E)", "أو رمز اللون (مثال: #1E1E2E)", "或颜色代码（例如 #1E1E2E）", "またはカラーコード（例: #1E1E2E）", "또는 색상 코드(예: #1E1E2E)");
            A("z.opacity", "Opacity: {0} %", "Opacité : {0} %", "Opacità: {0} %", "Deckkraft: {0} %", "Opacidad: {0} %", "Opacidade: {0} %", "الشفافية: {0}٪", "不透明度：{0} %", "不透明度: {0} %", "불투명도: {0} %");
            A("z.titleFont", "Title font", "Police du titre", "Carattere del titolo", "Schriftart des Titels", "Fuente del título", "Fonte do título", "خط العنوان", "标题字体", "タイトルのフォント", "제목 글꼴");
            A("z.itemFont", "Icon name font", "Police du nom des icônes", "Carattere dei nomi delle icone", "Schriftart der Symbolnamen", "Fuente de los nombres de iconos", "Fonte dos nomes dos ícones", "خط أسماء الأيقونات", "图标名称字体", "アイコン名のフォント", "아이콘 이름 글꼴");
            A("z.titleFontSize", "Title font size", "Taille de la police du titre", "Dimensione del carattere del titolo", "Schriftgröße des Titels", "Tamaño de fuente del título", "Tamanho da fonte do título", "حجم خط العنوان", "标题字体大小", "タイトルのフォントサイズ", "제목 글꼴 크기");
            A("z.itemFontSize", "Icon name font size", "Taille de la police des noms des icônes", "Dimensione del carattere dei nomi delle icone", "Schriftgröße der Symbolnamen", "Tamaño de fuente de los nombres de iconos", "Tamanho da fonte dos nomes dos ícones", "حجم خط أسماء الأيقونات", "图标名称字体大小", "アイコン名のフォントサイズ", "아이콘 이름 글꼴 크기");
            A("z.defaultFont", "(Default font)", "(Police par défaut)", "(Carattere predefinito)", "(Standardschriftart)", "(Fuente predeterminada)", "(Fonte padrão)", "(الخط الافتراضي)", "（默认字体）", "（既定のフォント）", "(기본 글꼴)");
            A("z.locked", "Locked (position and size)", "Verrouillée (position et taille)", "Bloccata (posizione e dimensioni)", "Gesperrt (Position und Größe)", "Bloqueada (posición y tamaño)", "Bloqueada (posição e tamanho)", "مقفلة (الموضع والحجم)", "已锁定（位置和大小）", "ロック（位置とサイズ）", "잠금(위치 및 크기)");
            A("z.rolled", "Rolled up", "Réduite (Roll Up)", "Ridotta (Roll Up)", "Eingeklappt (Roll Up)", "Contraída (Roll Up)", "Recolhida (Roll Up)", "مطويّة (Roll Up)", "已折叠 (Roll Up)", "折りたたみ (Roll Up)", "접힘 (Roll Up)");
            A("z.hover", "Expand on hover (if the global option is on)", "Développer au survol de la souris (si l'option globale est active)", "Espandi al passaggio del mouse (se l'opzione globale è attiva)", "Bei Mauskontakt ausklappen (wenn die globale Option aktiv ist)", "Expandir al pasar el ratón (si la opción global está activa)", "Expandir ao passar o mouse (se a opção global estiver ativa)", "التوسيع عند تمرير الماوس (إذا كان الخيار العام مفعّلاً)", "鼠标悬停时展开（需开启全局选项）", "マウスを重ねると展開（全体設定がオンの場合）", "마우스를 올리면 펼치기(전체 옵션이 켜져 있을 때)");
            A("z.applyAll", "Apply appearance (color, opacity, icons, fonts) to all zones", "Appliquer l'apparence (couleur, opacité, icônes, polices) à toutes les zones", "Applica l'aspetto (colore, opacità, icone, caratteri) a tutte le zone", "Darstellung (Farbe, Deckkraft, Symbole, Schriftarten) auf alle Bereiche anwenden", "Aplicar la apariencia (color, opacidad, iconos, fuentes) a todas las zonas", "Aplicar a aparência (cor, opacidade, ícones, fontes) a todas as zonas", "تطبيق المظهر (اللون والشفافية والأيقونات والخطوط) على كل المناطق", "将外观（颜色、不透明度、图标、字体）应用到所有区域", "外観（色・不透明度・アイコン・フォント）をすべてのゾーンに適用", "모양(색상, 불투명도, 아이콘, 글꼴)을 모든 영역에 적용");
            A("z.infoClassic", "Classic zone: {0} item(s).", "Zone classique : {0} élément(s).", "Zona classica: {0} elemento/i.", "Klassischer Bereich: {0} Element(e).", "Zona clásica: {0} elemento(s).", "Zona clássica: {0} item(ns).", "منطقة عادية: {0} عنصر.", "普通区域：{0} 项。", "通常のゾーン: {0} 個の項目。", "일반 영역: 항목 {0}개.");
            A("z.infoPortal", "Folder Portal: {0}", "Folder Portal : {0}", "Folder Portal: {0}", "Folder Portal: {0}", "Folder Portal: {0}", "Folder Portal: {0}", "Folder Portal: {0}", "Folder Portal：{0}", "Folder Portal: {0}", "Folder Portal: {0}");

            A("b.hover", "Expand a rolled-up zone when the mouse is over it, collapse it when the mouse leaves", "Développer une zone réduite quand la souris la survole, la réduire quand la souris la quitte", "Espandi una zona ridotta quando il mouse è sopra, riducila quando il mouse esce", "Eingeklappten Bereich bei Mauskontakt ausklappen und beim Verlassen wieder einklappen", "Expandir una zona contraída al pasar el ratón y contraerla al salir", "Expandir uma zona recolhida ao passar o mouse e recolhê-la ao sair", "توسيع المنطقة المطويّة عند مرور الماوس فوقها وطيّها عند مغادرته", "鼠标移到已折叠的区域上时展开，移开时再折叠", "折りたたんだゾーンにマウスを重ねると展開し、離れると折りたたむ", "접힌 영역에 마우스를 올리면 펼치고, 벗어나면 다시 접기");
            A("b.expandDelay", "Delay before expanding: {0} ms", "Délai avant développement : {0} ms", "Ritardo prima dell'espansione: {0} ms", "Verzögerung vor dem Ausklappen: {0} ms", "Retraso antes de expandir: {0} ms", "Atraso antes de expandir: {0} ms", "التأخير قبل التوسيع: {0} مللي ثانية", "展开前延迟：{0} 毫秒", "展開までの遅延: {0} ms", "펼치기 전 지연: {0} ms");
            A("b.collapseDelay", "Delay before collapsing: {0} ms", "Délai avant réduction : {0} ms", "Ritardo prima della riduzione: {0} ms", "Verzögerung vor dem Einklappen: {0} ms", "Retraso antes de contraer: {0} ms", "Atraso antes de recolher: {0} ms", "التأخير قبل الطيّ: {0} مللي ثانية", "折叠前延迟：{0} 毫秒", "折りたたみまでの遅延: {0} ms", "접기 전 지연: {0} ms");
            A("b.animate", "Animate the Roll (collapsing and expanding zones)", "Animer le Roll (réduction et développement des zones)", "Anima il Roll (riduzione ed espansione delle zone)", "Roll animieren (Ein- und Ausklappen der Bereiche)", "Animar el Roll (contraer y expandir zonas)", "Animar o Roll (recolher e expandir zonas)", "تحريك الطيّ (طيّ المناطق وتوسيعها)", "为折叠/展开添加动画", "Roll をアニメーション表示（ゾーンの折りたたみと展開）", "Roll 애니메이션(영역 접기 및 펼치기)");
            A("b.animMs", "Animation duration: {0} ms", "Durée de l'animation : {0} ms", "Durata dell'animazione: {0} ms", "Animationsdauer: {0} ms", "Duración de la animación: {0} ms", "Duração da animação: {0} ms", "مدة الحركة: {0} مللي ثانية", "动画时长：{0} 毫秒", "アニメーションの長さ: {0} ms", "애니메이션 지속 시간: {0} ms");
            A("b.hint",
                "A zone must first be rolled up (double-click an empty part of its title bar, or right-click > Roll up). Double-clicking the title bar of a zone expanded by hover keeps it expanded. The option can also be turned off zone by zone (Zones tab or right-click).",
                "Une zone doit d'abord être réduite (double-clic sur une partie vide de sa barre de titre, ou clic droit > Réduire). Un double-clic sur la barre de titre d'une zone développée par survol la garde développée. L'option peut aussi être désactivée zone par zone (onglet Zones ou clic droit).",
                "Una zona deve prima essere ridotta (doppio clic su una parte vuota della barra del titolo, oppure clic destro > Riduci). Un doppio clic sulla barra del titolo di una zona espansa al passaggio del mouse la mantiene espansa. L'opzione può essere disattivata anche zona per zona (scheda Zone o clic destro).",
                "Ein Bereich muss zuerst eingeklappt sein (Doppelklick auf einen leeren Teil der Titelleiste oder Rechtsklick > Einklappen). Ein Doppelklick auf die Titelleiste eines per Mauskontakt ausgeklappten Bereichs lässt ihn ausgeklappt. Die Option kann auch pro Bereich deaktiviert werden (Registerkarte Bereiche oder Rechtsklick).",
                "Una zona debe estar contraída primero (doble clic en una parte vacía de su barra de título, o clic derecho > Contraer). Un doble clic en la barra de título de una zona expandida al pasar el ratón la mantiene expandida. La opción también se puede desactivar zona por zona (pestaña Zonas o clic derecho).",
                "Uma zona precisa estar recolhida primeiro (clique duplo numa parte vazia da barra de título, ou botão direito > Recolher). Um clique duplo na barra de título de uma zona expandida ao passar o mouse a mantém expandida. A opção também pode ser desativada zona a zona (guia Zonas ou botão direito).",
                "يجب أولاً طيّ المنطقة (نقر مزدوج على جزء فارغ من شريط العنوان، أو نقر بزر الماوس الأيمن > طيّ). النقر المزدوج على شريط عنوان منطقة تم توسيعها بالتمرير يُبقيها موسّعة. يمكن أيضًا تعطيل الخيار لكل منطقة (علامة التبويب المناطق أو النقر بزر الماوس الأيمن).",
                "区域必须先折叠（双击标题栏的空白处，或右键 > 折叠）。双击因悬停而展开的区域的标题栏会使其保持展开。也可以逐个区域关闭此选项（“区域”选项卡或右键菜单）。",
                "ゾーンを先に折りたたんでおく必要があります（タイトルバーの空白部分をダブルクリック、または右クリック > 折りたたむ）。ホバーで展開したゾーンのタイトルバーをダブルクリックすると、展開したままになります。このオプションはゾーンごとにオフにすることもできます（「ゾーン」タブまたは右クリック）。",
                "먼저 영역을 접어야 합니다(제목 표시줄의 빈 부분을 더블 클릭하거나 마우스 오른쪽 버튼 > 접기). 마우스 오버로 펼쳐진 영역의 제목 표시줄을 더블 클릭하면 펼친 상태가 유지됩니다. 이 옵션은 영역별로 끌 수도 있습니다(‘영역’ 탭 또는 오른쪽 클릭).");

            A("a.defaults", "Defaults for new zones", "Valeurs par défaut des nouvelles zones", "Valori predefiniti per le nuove zone", "Standardwerte für neue Bereiche", "Valores predeterminados de las zonas nuevas", "Valores padrão das novas zonas", "القيم الافتراضية للمناطق الجديدة", "新区域的默认值", "新しいゾーンの既定値", "새 영역 기본값");
            A("a.apply", "Apply these values to all existing zones", "Appliquer ces valeurs à toutes les zones existantes", "Applica questi valori a tutte le zone esistenti", "Diese Werte auf alle vorhandenen Bereiche anwenden", "Aplicar estos valores a todas las zonas existentes", "Aplicar esses valores a todas as zonas existentes", "تطبيق هذه القيم على كل المناطق الحالية", "将这些值应用到所有现有区域", "これらの値を既存のすべてのゾーンに適用", "이 값을 기존 모든 영역에 적용");
            A("a.common", "Style shared by all zones", "Style commun à toutes les zones", "Stile comune a tutte le zone", "Für alle Bereiche gemeinsamer Stil", "Estilo común a todas las zonas", "Estilo comum a todas as zonas", "نمط مشترك لجميع المناطق", "所有区域共用的样式", "すべてのゾーン共通のスタイル", "모든 영역 공통 스타일");
            A("a.radius", "Corner radius: {0} px", "Arrondi des coins : {0} px", "Raggio degli angoli: {0} px", "Eckenradius: {0} px", "Radio de las esquinas: {0} px", "Raio dos cantos: {0} px", "استدارة الزوايا: {0} بكسل", "圆角半径：{0} px", "角の丸み: {0} px", "모서리 반경: {0}px");
            A("a.border", "Show zone borders", "Afficher la bordure des zones", "Mostra i bordi delle zone", "Rahmen der Bereiche anzeigen", "Mostrar el borde de las zonas", "Mostrar a borda das zonas", "إظهار حدود المناطق", "显示区域边框", "ゾーンの枠線を表示", "영역 테두리 표시");

            A("k.hint",
                "The layout is saved automatically (with one backup per day, 14 kept). A snapshot freezes a layout so you can come back to it later.",
                "La disposition est enregistrée automatiquement (avec une sauvegarde par jour, 14 conservées). Un instantané permet de figer une disposition et d'y revenir plus tard.",
                "La disposizione viene salvata automaticamente (con un backup al giorno, ne vengono conservati 14). Un'istantanea fissa una disposizione per ripristinarla in seguito.",
                "Das Layout wird automatisch gespeichert (mit einer Sicherung pro Tag, 14 werden aufbewahrt). Mit einer Momentaufnahme lässt sich ein Layout festhalten und später wiederherstellen.",
                "El diseño se guarda automáticamente (con una copia al día, se conservan 14). Una instantánea permite fijar un diseño y volver a él más tarde.",
                "O layout é salvo automaticamente (com um backup por dia, 14 são mantidos). Um instantâneo congela um layout para você voltar a ele depois.",
                "يُحفظ التخطيط تلقائيًا (مع نسخة احتياطية يوميًا، ويتم الاحتفاظ بـ 14). تتيح اللقطة تثبيت تخطيط والعودة إليه لاحقًا.",
                "布局会自动保存（每天一个备份，保留 14 个）。快照可以固定当前布局，以便日后恢复。",
                "レイアウトは自動的に保存されます（1 日 1 回バックアップ、14 件を保持）。スナップショットでレイアウトを保存し、後で元に戻せます。",
                "레이아웃은 자동으로 저장됩니다(하루 한 번 백업, 14개 보관). 스냅샷으로 레이아웃을 고정해 나중에 복원할 수 있습니다.");
            A("k.snapshot", "Create a snapshot now", "Créer un instantané maintenant", "Crea un'istantanea ora", "Jetzt Momentaufnahme erstellen", "Crear una instantánea ahora", "Criar um instantâneo agora", "إنشاء لقطة الآن", "立即创建快照", "今すぐスナップショットを作成", "지금 스냅샷 만들기");
            A("k.restore", "Restore a snapshot…", "Restaurer un instantané…", "Ripristina un'istantanea…", "Momentaufnahme wiederherstellen…", "Restaurar una instantánea…", "Restaurar um instantâneo…", "استعادة لقطة…", "还原快照…", "スナップショットを復元…", "스냅샷 복원…");
            A("k.path", "Configuration folder: {0}", "Dossier de configuration : {0}", "Cartella di configurazione: {0}", "Konfigurationsordner: {0}", "Carpeta de configuración: {0}", "Pasta de configuração: {0}", "مجلد الإعدادات: {0}", "配置文件夹：{0}", "設定フォルダー: {0}", "구성 폴더: {0}");
            A("k.snapshotOk", "Snapshot created:\n{0}", "Instantané créé :\n{0}", "Istantanea creata:\n{0}", "Momentaufnahme erstellt:\n{0}", "Instantánea creada:\n{0}", "Instantâneo criado:\n{0}", "تم إنشاء اللقطة:\n{0}", "快照已创建：\n{0}", "スナップショットを作成しました:\n{0}", "스냅샷을 만들었습니다:\n{0}");
            A("k.snapshotFail", "The snapshot could not be created.", "L'instantané n'a pas pu être créé.", "Impossibile creare l'istantanea.", "Die Momentaufnahme konnte nicht erstellt werden.", "No se pudo crear la instantánea.", "Não foi possível criar o instantâneo.", "تعذّر إنشاء اللقطة.", "无法创建快照。", "スナップショットを作成できませんでした。", "스냅샷을 만들 수 없습니다.");
            A("k.restoreTitle", "Choose a snapshot to restore", "Choisir un instantané à restaurer", "Scegli un'istantanea da ripristinare", "Momentaufnahme zum Wiederherstellen auswählen", "Elija una instantánea para restaurar", "Escolha um instantâneo para restaurar", "اختر لقطة لاستعادتها", "选择要还原的快照", "復元するスナップショットを選択", "복원할 스냅샷 선택");
            A("k.restoreConfirm",
                "The current layout will be replaced (a safety snapshot is created first).\n\nContinue?",
                "La disposition actuelle sera remplacée (un instantané de sécurité est créé avant).\n\nContinuer ?",
                "La disposizione attuale verrà sostituita (prima viene creata un'istantanea di sicurezza).\n\nContinuare?",
                "Das aktuelle Layout wird ersetzt (vorher wird eine Sicherheits-Momentaufnahme erstellt).\n\nFortfahren?",
                "Se reemplazará el diseño actual (antes se crea una instantánea de seguridad).\n\n¿Continuar?",
                "O layout atual será substituído (um instantâneo de segurança é criado antes).\n\nContinuar?",
                "سيتم استبدال التخطيط الحالي (يتم إنشاء لقطة أمان أولاً).\n\nمتابعة؟",
                "当前布局将被替换（会先创建一个安全快照）。\n\n是否继续？",
                "現在のレイアウトが置き換えられます（先に安全用のスナップショットを作成します）。\n\n続行しますか？",
                "현재 레이아웃이 교체됩니다(먼저 안전 스냅샷을 만듭니다).\n\n계속하시겠습니까?");
            A("k.restoreInvalid", "This file is not a valid snapshot.", "Ce fichier n'est pas un instantané valide.", "Questo file non è un'istantanea valida.", "Diese Datei ist keine gültige Momentaufnahme.", "Este archivo no es una instantánea válida.", "Este arquivo não é um instantâneo válido.", "هذا الملف ليس لقطة صالحة.", "此文件不是有效的快照。", "このファイルは有効なスナップショットではありません。", "유효한 스냅샷 파일이 아닙니다.");
            A("k.filter", "Layouts (*.json)|*.json", "Dispositions (*.json)|*.json", "Disposizioni (*.json)|*.json", "Layouts (*.json)|*.json", "Diseños (*.json)|*.json", "Layouts (*.json)|*.json", "التخطيطات (*.json)|*.json", "布局 (*.json)|*.json", "レイアウト (*.json)|*.json", "레이아웃 (*.json)|*.json");
            A("ab.version", "Version {0}", "Version {0}", "Versione {0}", "Version {0}", "Versión {0}", "Versão {0}", "الإصدار {0}", "版本 {0}", "バージョン {0}", "버전 {0}");
            A("ab.text", "Organize your desktop with zones for shortcuts, files and folders.", "Organisez votre bureau avec des zones pour regrouper raccourcis, fichiers et dossiers.", "Organizza il desktop con zone per raccogliere collegamenti, file e cartelle.", "Organisieren Sie Ihren Desktop mit Bereichen für Verknüpfungen, Dateien und Ordner.", "Organiza tu escritorio con zonas para agrupar accesos directos, archivos y carpetas.", "Organize sua área de trabalho com zonas para agrupar atalhos, arquivos e pastas.", "نظّم سطح المكتب باستخدام مناطق لتجميع الاختصارات والملفات والمجلدات.", "使用桌面区域整理快捷方式、文件和文件夹。", "デスクトップ上のゾーンでショートカット、ファイル、フォルダーを整理するアプリです。", "바탕 화면의 영역으로 바로 가기, 파일, 폴더를 정리하는 앱입니다.");
        }
    }
}
