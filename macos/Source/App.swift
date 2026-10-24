import AppKit

struct Server: Decodable {
    let alias: String
    let group: String
    let host: String
    let account: String
    let port: String
    let key: String
    let credentials: [String]
    let operations: [String]
    var sudoSameAsLogin: Bool? = nil
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSTableViewDataSource, NSTableViewDelegate {
    var statusItem: NSStatusItem!
    var servers: [Server] = []
    var displayed: [Server] = []
    var search: NSSearchField!
    var groupPicker: NSPopUpButton!
    var emptyLabel: NSTextField!
    var manager: NSWindow!
    var table: NSTableView!
    var editors: [Editor] = []
    var jobs = 0
    var footer: NSTextField!
    var diagnosticPath: String? {
        let args = CommandLine.arguments
        guard let index = args.firstIndex(of: "--smoke-test"), args.indices.contains(index+1) else { return nil }
        return args[index+1]
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        let mainMenu = NSMenu()
        let appRoot = NSMenuItem(title: "服务器管理", action: nil, keyEquivalent: "")
        let appMenu = NSMenu(title: "服务器管理")
        let about = NSMenuItem(title: "关于服务器管理", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        about.target = NSApp; appMenu.addItem(about); appMenu.addItem(.separator())
        let quitItem = NSMenuItem(title: "退出服务器管理", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self; appMenu.addItem(quitItem); appRoot.submenu = appMenu; mainMenu.addItem(appRoot)
        let fileRoot = NSMenuItem(title: "文件", action: nil, keyEquivalent: "")
        let fileMenu = NSMenu(title: "文件")
        let newItem = NSMenuItem(title: "新增服务器…", action: #selector(add), keyEquivalent: "n"); newItem.target = self; fileMenu.addItem(newItem)
        let refreshItem = NSMenuItem(title: "刷新列表", action: #selector(refresh), keyEquivalent: "r"); refreshItem.target = self; fileMenu.addItem(refreshItem)
        fileRoot.submenu = fileMenu; mainMenu.addItem(fileRoot)
        let editRoot = NSMenuItem(title: "编辑", action: nil, keyEquivalent: "")
        let editMenu = NSMenu(title: "编辑")
        for (title, selector, key) in [("剪切", "cut:", "x"), ("复制", "copy:", "c"), ("粘贴", "paste:", "v"), ("全选", "selectAll:", "a")] {
            editMenu.addItem(NSMenuItem(title: title, action: Selector(selector), keyEquivalent: key))
        }
        editRoot.submenu = editMenu; mainMenu.addItem(editRoot); NSApp.mainMenu = mainMenu
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "server.rack", accessibilityDescription: "服务器管理")
        statusItem.button?.toolTip = "服务器管理"
        statusItem.button?.title = ""
        buildManager()
        rebuildMenu()
        reload {
            self.showManager()
            if let path = self.diagnosticPath {
                self.servers = [Server(alias: "smoke-work", group: "工作", host: "example.invalid", account: "deploy", port: "22", key: "", credentials: [], operations: []), Server(alias: "smoke-home", group: "家用", host: "example.invalid", account: "deploy", port: "22", key: "", credentials: [], operations: [])]
                self.updateFilters(); self.rebuildMenu()
                let editor = Editor(owner: self, server: nil)
                editor.window.contentView?.layoutSubtreeIfNeeded()
                self.groupPicker.selectItem(withTitle: "工作"); self.applyFilter()
                let groupFiltering = self.displayed.count == 1 && self.displayed[0].alias == "smoke-work"
                self.groupPicker.selectItem(at: 0); self.search.stringValue = "home"; self.applyFilter()
                let searchFiltering = self.displayed.count == 1 && self.displayed[0].alias == "smoke-home"
                self.search.stringValue = ""; self.applyFilter()
                let sudoInitiallyOff = editor.sudoSameButton.state == .off && !editor.sudoSameButton.isEnabled
                editor.fields["login"]!.stringValue = "synthetic-test-only"; editor.updateSudoMode()
                let sudoDefaultsOn = editor.sudoSameButton.state == .on && !editor.fields["sudo"]!.isEnabled
                editor.sudoSameButton.state = .off; editor.sudoModeChanged(nil)
                editor.fields["login"]!.stringValue = "synthetic-change-only"; editor.updateSudoMode()
                let sudoManualChoice = editor.sudoSameButton.state == .off && editor.fields["sudo"]!.isEnabled
                editor.sudoSameButton.state = .on; editor.sudoModeChanged(nil)
                editor.fields["key"]!.stringValue = "/synthetic/private-key"; editor.updateSudoMode()
                let sudoKeyModeOff = editor.sudoSameButton.state == .off && !editor.sudoSameButton.isEnabled
                editor.fields["key"]!.stringValue = ""; editor.fields["login"]!.stringValue = ""
                editor.updateSudoMode()
                self.editors.append(editor); editor.show()
                let editorAttached = editor.window.sheetParent === self.manager && self.manager.attachedSheet === editor.window
                self.showEditor(self.servers.first)
                let oneEditorOnly = self.editors.count == 1 && !self.canStartAction()
                let child = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 220, height: 100), styleMask: [.titled], backing: .buffered, defer: false)
                editor.window.beginSheet(child)
                let nestedSheet = child.sheetParent === editor.window && editor.window.sheetParent === self.manager
                editor.window.endSheet(child); child.orderOut(nil)
                let proof: [String: Any] = ["statusItemVisible": self.statusItem.isVisible,
                    "managerVisible": self.manager.isVisible, "serverCount": self.servers.count,
                    "nativeWindow": true, "webServerStarted": false, "backendResponded": true,
                    "groupsPresent": self.statusItem.menu!.items.filter { ["工作", "家用"].contains($0.title) }.count == 2,
                    "editorAttachedToManager": editorAttached, "singleEditorEnforced": oneEditorOnly, "nestedSheetHierarchy": nestedSheet,
                    "sudoInitiallyOff": sudoInitiallyOff, "sudoDefaultsOn": sudoDefaultsOn,
                    "sudoManualChoice": sudoManualChoice, "sudoKeyModeOff": sudoKeyModeOff,
                    "aliasStartsEmpty": editor.fields["alias"]!.stringValue.isEmpty,
                    "nativeSecureFields": ["login", "passphrase", "sudo"].allSatisfy { editor.fields[$0] is NSSecureTextField },
                    "aliasFieldHeight": editor.fields["alias"]!.frame.height,
                    "groupFiltering": groupFiltering, "searchFiltering": searchFiltering,
                    "commandTabEligible": NSApp.activationPolicy() == .regular,
                    "filePickerPresent": editor.keyChooserButton != nil && !editor.fields["key"]!.isEditable,
                    "iconPackaged": Bundle.main.path(forResource: "AppIcon", ofType: "icns") != nil,
                    "sftpMenuPresent": self.statusItem.menu!.items.compactMap { $0.submenu }.flatMap { $0.items }.compactMap { $0.submenu }.contains { $0.items.contains { $0.title == "打开 SFTP" } } ]
                if let data = try? JSONSerialization.data(withJSONObject: proof, options: [.prettyPrinted, .sortedKeys]) {
                    try? data.write(to: URL(fileURLWithPath: path), options: .atomic)
                }
                DispatchQueue.main.asyncAfter(deadline: .now()+0.5) { editor.dismiss(); NSApp.terminate(nil) }
            }
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showManager(); return true }

    func application(_ application: NSApplication, open urls: [URL]) {
        if urls.contains(where: { $0.host == "new" }) { add(nil) }
        else { showManager() }
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if jobs > 0 { alert("操作正在进行", "请等待当前保存、连接测试或刷新完成，再退出工具。"); return .terminateCancel }
        return .terminateNow
    }

    func alert(_ title: String, _ message: String) {
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = message
        showManager()
        let parent = editors.first?.window ?? manager!
        if parent.attachedSheet != nil { footer.stringValue = "\(title)：\(message)"; return }
        alert.beginSheetModal(for: parent)
    }

    func call(_ request: [String: Any], done: @escaping ([String: Any]) -> Void) {
        jobs += 1
        let python = ["/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/Library/Frameworks/Python.framework/Versions/Current/bin/python3", "/usr/bin/python3"].first { FileManager.default.isExecutableFile(atPath: $0) } ?? "/usr/bin/python3"
        let bridge = Bundle.main.resourceURL!.appendingPathComponent("scripts/native_bridge.py").path
        DispatchQueue.global(qos: .userInitiated).async {
            var response: [String: Any]
            do {
                let process = Process(); process.executableURL = URL(fileURLWithPath: python)
                process.arguments = ["-B", bridge]
                let input = Pipe(), output = Pipe()
                process.standardInput = input; process.standardOutput = output; process.standardError = FileHandle.nullDevice
                var environment = ProcessInfo.processInfo.environment
                environment["PYTHONDONTWRITEBYTECODE"] = "1"; process.environment = environment
                try process.run()
                let data = try JSONSerialization.data(withJSONObject: request)
                input.fileHandleForWriting.write(data); try? input.fileHandleForWriting.close()
                let result = output.fileHandleForReading.readDataToEndOfFile()
                process.waitUntilExit()
                response = (try? JSONSerialization.jsonObject(with: result)) as? [String: Any]
                    ?? ["ok": false, "message": "本地配置助手未能返回结果，请确认 Python 3 和钥匙串权限可用。"]
            } catch { response = ["ok": false, "message": "无法启动本地配置助手，请检查 Python 3 路径和应用安装完整性。"] }
            DispatchQueue.main.async { self.jobs -= 1; done(response) }
        }
    }

    func reload(_ completion: (() -> Void)? = nil) {
        call(["action": "list"]) { result in
            if result["ok"] as? Bool == true, let values = result["servers"],
               let data = try? JSONSerialization.data(withJSONObject: values),
               let decoded = try? JSONDecoder().decode([Server].self, from: data) {
                self.servers = decoded
                self.updateFilters(); self.rebuildMenu()

                completion?()
            } else { self.alert("无法读取服务器列表", result["message"] as? String ?? "配置文件可能不完整。") }
        }
    }

    func item(_ title: String, _ action: Selector, alias: String? = nil) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self; item.representedObject = alias; return item
    }

    func rebuildMenu() {
        let menu = NSMenu()
        menu.addItem(item("打开服务器管理…", #selector(openManager)))
        menu.addItem(item("新增服务器…", #selector(add)))
        menu.addItem(.separator())
        if servers.isEmpty { let empty = NSMenuItem(title: "尚未配置服务器", action: nil, keyEquivalent: ""); empty.isEnabled = false; menu.addItem(empty) }
        for group in Set(servers.map { $0.group }).sorted() {
            let groupItem = NSMenuItem(title: group, action: nil, keyEquivalent: "")
            let groupMenu = NSMenu()
            for server in servers.filter({ $0.group == group }) {
                let serverItem = NSMenuItem(title: server.alias, action: nil, keyEquivalent: "")
                let submenu = NSMenu()
                let address = NSMenuItem(title: "\(server.account)@\(server.host):\(server.port)", action: nil, keyEquivalent: "")
                address.isEnabled = false; submenu.addItem(address)
                submenu.addItem(item("查看 / 编辑配置…", #selector(editMenu), alias: server.alias))
                submenu.addItem(item("测试连接", #selector(testMenu), alias: server.alias))
                submenu.addItem(item("在终端中连接", #selector(terminalMenu), alias: server.alias))
                submenu.addItem(item("打开 SFTP", #selector(sftpMenu), alias: server.alias))
                submenu.addItem(.separator())
                submenu.addItem(item("删除本机配置…", #selector(deleteMenu), alias: server.alias))
                serverItem.submenu = submenu; groupMenu.addItem(serverItem)
            }
            groupItem.submenu = groupMenu; menu.addItem(groupItem)
        }
        menu.addItem(.separator())
        menu.addItem(item("刷新列表", #selector(refresh)))
        menu.addItem(item("退出服务器管理", #selector(quit)))
        statusItem.menu = menu
    }

    func buildManager() {
        manager = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 880, height: 540), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        manager.title = "服务器管理"; manager.isReleasedWhenClosed = false; manager.minSize = NSSize(width: 820, height: 440); manager.center()
        let root = NSStackView(); root.orientation = .vertical; root.spacing = 16; root.alignment = .leading
        root.edgeInsets = NSEdgeInsets(top: 24, left: 24, bottom: 18, right: 24)
        root.translatesAutoresizingMaskIntoConstraints = false; manager.contentView!.addSubview(root)
        NSLayoutConstraint.activate([root.topAnchor.constraint(equalTo: manager.contentView!.topAnchor), root.bottomAnchor.constraint(equalTo: manager.contentView!.bottomAnchor), root.leadingAnchor.constraint(equalTo: manager.contentView!.leadingAnchor), root.trailingAnchor.constraint(equalTo: manager.contentView!.trailingAnchor)])
        let title = NSTextField(labelWithString: "我的服务器"); title.font = .systemFont(ofSize: 26, weight: .semibold); root.addArrangedSubview(title)
        let hint = NSTextField(labelWithString: "管理连接与分组 · 密码保存在钥匙串 · 支持 SSH 与 SFTP")
        hint.textColor = .secondaryLabelColor; root.addArrangedSubview(hint)
        let filters = NSStackView(); filters.spacing = 12
        search = NSSearchField(); search.placeholderString = "搜索别名、地址、用户或分组"; search.target = self; search.action = #selector(filterChanged); search.sendsSearchStringImmediately = true
        search.setContentHuggingPriority(.defaultLow, for: .horizontal)
        groupPicker = NSPopUpButton(); groupPicker.addItem(withTitle: "全部分组"); groupPicker.target = self; groupPicker.action = #selector(filterChanged)
        groupPicker.widthAnchor.constraint(equalToConstant: 170).isActive = true
        filters.addArrangedSubview(search); filters.addArrangedSubview(groupPicker); root.addArrangedSubview(filters)
        filters.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -48).isActive = true
        table = NSTableView(); table.delegate = self; table.dataSource = self; table.rowHeight = 56
        table.style = .fullWidth; table.usesAlternatingRowBackgroundColors = true; table.intercellSpacing = NSSize(width: 12, height: 2)
        for (id, title, width) in [("server", "服务器 / 连接地址", 460.0), ("group", "分组", 170.0), ("auth", "认证方式", 140.0)] {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(id)); column.title = title; column.width = width; table.addTableColumn(column)
        }
        table.target = self; table.doubleAction = #selector(editSelected)
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true; scroll.borderType = .noBorder
        let list = NSView(); list.wantsLayer = true; list.layer?.cornerRadius = 10; list.layer?.borderWidth = 1; list.layer?.borderColor = NSColor.separatorColor.cgColor
        list.addSubview(scroll); scroll.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([scroll.leadingAnchor.constraint(equalTo: list.leadingAnchor), scroll.trailingAnchor.constraint(equalTo: list.trailingAnchor), scroll.topAnchor.constraint(equalTo: list.topAnchor), scroll.bottomAnchor.constraint(equalTo: list.bottomAnchor)])
        emptyLabel = NSTextField(wrappingLabelWithString: "还没有服务器\n点击“新增”配置你的第一台服务器")
        emptyLabel.alignment = .center; emptyLabel.textColor = .secondaryLabelColor; emptyLabel.translatesAutoresizingMaskIntoConstraints = false
        list.addSubview(emptyLabel); NSLayoutConstraint.activate([emptyLabel.centerXAnchor.constraint(equalTo: list.centerXAnchor), emptyLabel.centerYAnchor.constraint(equalTo: list.centerYAnchor), emptyLabel.widthAnchor.constraint(equalToConstant: 360)])
        root.addArrangedSubview(list); list.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -48).isActive = true; list.heightAnchor.constraint(greaterThanOrEqualToConstant: 230).isActive = true
        let buttons = NSStackView(); buttons.spacing = 8
        for (title, action, symbol) in [("新增", #selector(add), "plus"), ("编辑", #selector(editSelected), "slider.horizontal.3"), ("测试", #selector(testSelected), "checkmark.circle"), ("SSH", #selector(terminalSelected), "terminal"), ("SFTP", #selector(sftpSelected), "folder"), ("删除", #selector(deleteSelected), "trash"), ("刷新", #selector(refresh), "arrow.clockwise")] {
            let button = NSButton(title: title, target: self, action: action); button.bezelStyle = .rounded
            button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil); button.imagePosition = .imageLeading
            if title == "新增" { button.keyEquivalent = "n"; button.keyEquivalentModifierMask = .command; button.contentTintColor = .controlAccentColor }
            buttons.addArrangedSubview(button)
        }
        root.addArrangedSubview(buttons)
        footer = NSTextField(labelWithString: "正在读取…"); footer.font = .systemFont(ofSize: 11); footer.textColor = .secondaryLabelColor; root.addArrangedSubview(footer)
    }

    func updateFilters() {
        let selected = groupPicker.titleOfSelectedItem
        groupPicker.removeAllItems(); groupPicker.addItem(withTitle: "全部分组")
        groupPicker.addItems(withTitles: Set(servers.map { $0.group }).sorted())
        if let selected = selected, groupPicker.itemTitles.contains(selected) { groupPicker.selectItem(withTitle: selected) }
        applyFilter()
    }
    func applyFilter() {
        let query = search.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let group = groupPicker.indexOfSelectedItem > 0 ? groupPicker.titleOfSelectedItem : nil
        displayed = servers.filter { server in
            (group == nil || server.group == group) && (query.isEmpty || [server.alias, server.host, server.account, server.group].contains { $0.localizedCaseInsensitiveContains(query) })
        }
        table.reloadData(); emptyLabel.isHidden = !displayed.isEmpty
        emptyLabel.stringValue = servers.isEmpty ? "还没有服务器\n点击“新增”配置你的第一台服务器" : "没有匹配的服务器\n请调整搜索内容或分组筛选"
        footer.stringValue = "显示 \(displayed.count) / \(servers.count) 台 · 双击查看配置 · ⌘N 新增"
    }
    @objc func filterChanged(_ sender: Any?) { applyFilter() }
    func numberOfRows(in tableView: NSTableView) -> Int { displayed.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let server = displayed[row]
        let cell = NSTableCellView(); let content: NSView
        if tableColumn?.identifier.rawValue == "server" {
            let icon = NSImageView(image: NSImage(systemSymbolName: "server.rack", accessibilityDescription: nil)!)
            icon.contentTintColor = .controlAccentColor; icon.widthAnchor.constraint(equalToConstant: 26).isActive = true; icon.heightAnchor.constraint(equalToConstant: 26).isActive = true
            let name = NSTextField(labelWithString: server.alias); name.font = .systemFont(ofSize: 13, weight: .semibold)
            let address = NSTextField(labelWithString: "\(server.account)@\(server.host):\(server.port)"); address.font = .monospacedSystemFont(ofSize: 11, weight: .regular); address.textColor = .secondaryLabelColor; address.lineBreakMode = .byTruncatingMiddle
            let labels = NSStackView(views: [name, address]); labels.orientation = .vertical; labels.alignment = .leading; labels.spacing = 4
            let row = NSStackView(views: [icon, labels]); row.spacing = 12; content = row
        } else {
            let label = NSTextField(labelWithString: tableColumn?.identifier.rawValue == "group" ? server.group : (server.key.isEmpty ? "密码" : "私钥"))
            label.textColor = .secondaryLabelColor; label.font = .systemFont(ofSize: 12); content = label
        }
        content.translatesAutoresizingMaskIntoConstraints = false; cell.addSubview(content)
        NSLayoutConstraint.activate([content.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 10), content.trailingAnchor.constraint(lessThanOrEqualTo: cell.trailingAnchor, constant: -10), content.centerYAnchor.constraint(equalTo: cell.centerYAnchor)])
        return cell
    }
    func selected() -> Server? { table.selectedRow >= 0 && table.selectedRow < displayed.count ? displayed[table.selectedRow] : nil }
    func byMenu(_ sender: NSMenuItem) -> Server? { servers.first { $0.alias == sender.representedObject as? String } }
    func showManager() {
        manager.makeKeyAndOrderFront(nil)
        var front = manager!
        while let sheet = front.attachedSheet { front = sheet }
        front.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
    func canStartAction() -> Bool {
        if manager.attachedSheet != nil { showManager(); return false }
        return true
    }
    func showEditor(_ server: Server?) {
        if let existing = editors.first { existing.show(); return }
        guard canStartAction() else { return }
        let editor = Editor(owner: self, server: server); editors.append(editor); editor.show()
    }
    func test(_ server: Server) {
        guard canStartAction() else { return }
        footer.stringValue = "正在测试 \(server.alias)…"
        call(["action":"test", "alias":server.alias]) { result in
            self.alert(result["ok"] as? Bool == true ? "连接成功" : "连接未通过", result["message"] as? String ?? "未知结果")
            self.reload()
        }
    }
    func connect(_ server: Server, sftp: Bool = false) { guard canStartAction() else { return }; call(["action":sftp ? "sftp" : "terminal", "alias":server.alias]) { result in if result["ok"] as? Bool != true { self.alert("无法打开连接", result["message"] as? String ?? "") } } }
    func remove(_ server: Server) {
        guard canStartAction() else { return }
        showManager()
        let alert = NSAlert(); alert.messageText = "删除 \(server.alias) 的本机配置？"
        alert.informativeText = "同时删除此配置对应的钥匙串条目。不会删除或关闭服务器，也不影响服务器上的文件。"
        alert.addButton(withTitle: "取消"); alert.addButton(withTitle: "删除")
        alert.beginSheetModal(for: manager) { response in
            guard response == .alertSecondButtonReturn else { return }
            self.call(["action":"delete", "alias":server.alias]) { result in
                if result["ok"] as? Bool != true { self.alert("删除未完成", result["message"] as? String ?? "") }
                self.reload()
            }
        }
    }
    @objc func openManager(_ sender: Any?) { showManager() }
    @objc func add(_ sender: Any?) { showEditor(nil) }
    @objc func refresh(_ sender: Any?) { if canStartAction() { reload() } }
    @objc func quit(_ sender: Any?) { NSApp.terminate(nil) }
    @objc func editSelected(_ sender: Any?) { if let server = selected() { showEditor(server) } }
    @objc func testSelected(_ sender: Any?) { if let server = selected() { test(server) } }
    @objc func terminalSelected(_ sender: Any?) { if let server = selected() { connect(server) } }
    @objc func sftpSelected(_ sender: Any?) { if let server = selected() { connect(server, sftp: true) } }
    @objc func deleteSelected(_ sender: Any?) { if let server = selected() { remove(server) } }
    @objc func editMenu(_ sender: NSMenuItem) { if let server = byMenu(sender) { showEditor(server) } }
    @objc func testMenu(_ sender: NSMenuItem) { if let server = byMenu(sender) { test(server) } }
    @objc func terminalMenu(_ sender: NSMenuItem) { if let server = byMenu(sender) { connect(server) } }
    @objc func sftpMenu(_ sender: NSMenuItem) { if let server = byMenu(sender) { connect(server, sftp: true) } }
    @objc func deleteMenu(_ sender: NSMenuItem) { if let server = byMenu(sender) { remove(server) } }
}

final class Editor: NSObject, NSWindowDelegate, NSTextFieldDelegate {
    unowned let owner: AppDelegate
    let server: Server?
    let window: NSWindow
    var fields: [String: NSTextField] = [:]
    var saveButton: NSButton!
    var progress: NSTextField!
    var working = false
    var keyChooserButton: NSButton!
    var sudoSameButton: NSButton!
    var sudoChoiceWasChanged = false

    init(owner: AppDelegate, server: Server?) {
        self.owner = owner; self.server = server
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 610, height: 730), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(); window.delegate = self; window.isReleasedWhenClosed = false
        window.title = server == nil ? "新增服务器" : "查看 / 编辑 · \(server!.alias)"; window.center()
        let root = NSStackView(); root.orientation = .vertical; root.spacing = 8; root.alignment = .leading
        root.edgeInsets = NSEdgeInsets(top: 20, left: 24, bottom: 20, right: 24)
        root.translatesAutoresizingMaskIntoConstraints = false; window.contentView!.addSubview(root)
        NSLayoutConstraint.activate([root.topAnchor.constraint(equalTo: window.contentView!.topAnchor), root.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor), root.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor), root.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor)])
        let hint = NSTextField(wrappingLabelWithString: "别名由你填写，仅限英文字母、数字、_ 和 -，以字母或数字开头。分组支持中文。密码只存入 Mac 钥匙串。")
        hint.textColor = .secondaryLabelColor; root.addArrangedSubview(hint)
        func field(_ key: String, _ title: String, _ value: String = "", secure: Bool = false, placeholder: String = "") {
            let label = NSTextField(labelWithString: title); label.font = .systemFont(ofSize: 12, weight: .medium); root.addArrangedSubview(label)
            let input: NSTextField
            if key == "group" {
                let combo = NSComboBox(); combo.addItems(withObjectValues: Array(Set(owner.servers.map { $0.group })).sorted()); input = combo
            } else { input = secure ? NSSecureTextField() : NSTextField() }
            input.stringValue = value; input.placeholderString = placeholder; fields[key] = input
            if key == "key" {
                input.isEditable = false; input.isSelectable = true; input.setContentHuggingPriority(.defaultLow, for: .horizontal)
                keyChooserButton = NSButton(title: "选择…", target: self, action: #selector(chooseKey))
                let clear = NSButton(title: "清除", target: self, action: #selector(clearKey))
                let row = NSStackView(views: [input, keyChooserButton, clear]); row.spacing = 8
                root.addArrangedSubview(row); row.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -48).isActive = true
            } else { root.addArrangedSubview(input); input.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -48).isActive = true }
        }
        field("alias", "服务器别名", server?.alias ?? "", placeholder: "例如 hk-relay、home-server")
        fields["alias"]?.isEditable = server == nil
        field("group", "分组", server?.group ?? "", placeholder: "例如工作、家用、测试；可直接输入新分组")
        field("host", "服务器域名或 IP", server?.host ?? "")
        field("account", "SSH 用户名", server?.account ?? "")
        field("port", "SSH 端口", server?.port ?? "22")
        field("key", "私钥文件（密码登录请留空）", server?.key ?? "", placeholder: "点击“选择…”从访达选择文件")
        field("login", "SSH 登录密码", secure: true, placeholder: server == nil ? "私钥登录留空" : "留空保留已保存密码")
        field("passphrase", "私钥解锁口令", secure: true, placeholder: server == nil ? "没有口令则留空" : "留空保留已保存口令")
        fields["login"]?.delegate = self
        sudoSameButton = NSButton(checkboxWithTitle: "sudo 与 SSH 登录密码一致", target: self, action: #selector(sudoModeChanged))
        sudoSameButton.state = server?.sudoSameAsLogin == true ? .on : .off
        sudoChoiceWasChanged = server != nil
        root.addArrangedSubview(sudoSameButton)
        field("sudo", "sudo 密码（可选）", secure: true, placeholder: server == nil ? "需要 sudo 时填写" : "留空保留已保存密码")
        progress = NSTextField(wrappingLabelWithString: "保存时先显示主机指纹，由你确认；随后验证连接。不会部署或修改服务器权限。")
        progress.font = .systemFont(ofSize: 11); progress.textColor = .secondaryLabelColor; root.addArrangedSubview(progress)
        saveButton = NSButton(title: "核对指纹并保存", target: self, action: #selector(save))
        let cancelButton = NSButton(title: "取消", target: self, action: #selector(cancel))
        cancelButton.keyEquivalent = "\u{1b}"
        let buttons = NSStackView(views: [cancelButton, saveButton]); buttons.spacing = 12
        root.addArrangedSubview(buttons)
        updateSudoMode()
    }
    @objc func chooseKey(_ sender: Any?) {
        guard !working else { return }
        let panel = NSOpenPanel(); panel.title = "选择 SSH 私钥"; panel.prompt = "选择私钥"
        panel.message = "选择私钥文件本身，不要选择 .pub 公钥。此处只记录路径。"
        panel.canChooseFiles = true; panel.canChooseDirectories = false; panel.allowsMultipleSelection = false; panel.showsHiddenFiles = true
        panel.directoryURL = URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent(".ssh")
        panel.beginSheetModal(for: window) { response in
            if response == .OK, let url = panel.url { self.fields["key"]?.stringValue = url.path; self.updateSudoMode() }
        }
    }
    @objc func clearKey(_ sender: Any?) { if !working { fields["key"]?.stringValue = ""; updateSudoMode() } }
    func controlTextDidChange(_ notification: Notification) { updateSudoMode() }
    @objc func sudoModeChanged(_ sender: Any?) { sudoChoiceWasChanged = true; updateSudoMode() }
    func updateSudoMode() {
        let passwordLogin = fields["key"]!.stringValue.isEmpty
        let hasLogin = !fields["login"]!.stringValue.isEmpty || (server?.credentials.contains("login") == true && fields["account"]!.stringValue == server?.account)
        let available = passwordLogin && hasLogin
        sudoSameButton.isEnabled = available && !working
        if !available { sudoSameButton.state = .off }
        else if !sudoChoiceWasChanged { sudoSameButton.state = .on }
        fields["sudo"]?.isEnabled = sudoSameButton.state != .on && !working
        fields["sudo"]?.placeholderString = sudoSameButton.state == .on ? "保存时使用 SSH 登录密码，不显示或复制密码" : (server == nil ? "需要 sudo 时填写" : "留空保留已保存密码")
    }
    func show() {
        if window.sheetParent == nil { owner.manager.beginSheet(window) }
        owner.showManager()
    }
    @objc func cancel(_ sender: Any?) { if !working { dismiss() } }
    func dismiss() {
        if let parent = window.sheetParent { parent.endSheet(window) }
        window.orderOut(nil); window.close()
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { if working { NSSound.beep(); return false }; return true }
    func windowWillClose(_ notification: Notification) {
        if let parent = window.sheetParent { parent.endSheet(window) }
        for kind in ["login", "passphrase", "sudo"] { fields[kind]?.stringValue = "" }
        owner.editors.removeAll { $0 === self }
    }
    @objc func save(_ sender: Any?) {
        guard !working else { return }
        window.makeFirstResponder(nil)
        updateSudoMode()
        var request: [String: Any] = [:]
        for (key, field) in fields { request[key] = field.stringValue }
        let alias = fields["alias"]!.stringValue
        if alias.range(of: "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", options: .regularExpression) == nil {
            fail("别名格式不正确", "仅支持英文字母、数字、下划线和短横线，以字母或数字开头，长度 1–64。不能包含中文或空格。")
            window.makeFirstResponder(fields["alias"]); return
        }
        request["sudo_same_as_login"] = sudoSameButton.state == .on
        request["original"] = server?.alias ?? ""
        var publicRequest = request
        for kind in ["login", "passphrase", "sudo"] { publicRequest.removeValue(forKey: kind) }
        publicRequest["action"] = "scan"
        working = true; updateSudoMode(); saveButton.isEnabled = false; progress.textColor = .secondaryLabelColor; progress.stringValue = "正在获取主机指纹…"
        owner.call(publicRequest) { result in
            guard result["ok"] as? Bool == true else {
                self.fail("无法获取指纹", result["message"] as? String ?? "本地助手未返回原因。"); return
            }
            let alert = NSAlert(); alert.messageText = "请独立核对服务器主机指纹"
            alert.informativeText = "别名：\(alias)\n请通过服务器控制台或可信管理员核对：\n\n\(result["fingerprints"] as? String ?? "")\n确认后保存凭据并仅验证 SSH / sudo，不执行部署。"
            alert.addButton(withTitle: "取消"); alert.addButton(withTitle: "已核对，保存并验证")
            alert.beginSheetModal(for: self.window) { response in
                guard response == .alertSecondButtonReturn else {
                    self.finish(); self.progress.stringValue = "已取消指纹确认，尚未保存。"; return
                }
                request["keys"] = result["keys"]; request["action"] = "save"
                self.progress.stringValue = "正在保存到钥匙串并验证 SSH / sudo，请稍候…"
                self.owner.call(request) { result in
                    self.finish()
                    if result["ok"] as? Bool == true {
                        for kind in ["login", "passphrase", "sudo"] { self.fields[kind]?.stringValue = "" }
                        self.dismiss()
                        self.owner.search.stringValue = ""; self.owner.groupPicker.selectItem(at: 0)
                        self.owner.reload {
                            self.owner.showManager()
                            if let row = self.owner.displayed.firstIndex(where: { $0.alias == alias }) {
                                self.owner.table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
                                self.owner.table.scrollRowToVisible(row)
                            }
                            self.owner.footer.stringValue = "已保存：\(alias) · SSH / sudo 验证通过"
                        }
                    } else { self.fail("保存未完成", result["message"] as? String ?? "本地助手未返回原因。") }
                }
            }
        }
    }
    func fail(_ title: String, _ message: String) {
        finish(); progress.textColor = .systemRed; progress.stringValue = "\(title)：\(message)"
        let alert = NSAlert(); alert.alertStyle = .warning; alert.messageText = title; alert.informativeText = message
        alert.beginSheetModal(for: window)
    }
    func finish() { working = false; updateSudoMode(); saveButton.isEnabled = true; progress.stringValue = "密码不会显示到日志。编辑时留空会保留已有密码。" }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
