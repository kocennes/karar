// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:html' as html;

abstract final class SwUpdateService {
  static const _storageKey = 'karar_sw_update';

  static void reload() => html.window.location.reload();

  static void listenForUpdates(void Function() onUpdate) {
    try {
      (html.window as dynamic).onSwUpdateReady = () {
        try {
          html.window.localStorage[_storageKey] =
              DateTime.now().millisecondsSinceEpoch.toString();
          onUpdate();
        } catch (_) {}
      };

      html.window.onStorage.listen((e) {
        if (e.key == _storageKey && e.newValue != null) {
          onUpdate();
        }
      });
    } catch (_) {}
  }
}
