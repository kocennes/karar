// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:html' as html;

abstract final class TabAuthSync {
  static const _key = 'karar_auth_state';

  // Login/logout sonrası diğer sekmelere sinyal gönder
  static void notifyLoggedIn() => html.window.localStorage[_key] =
      DateTime.now().millisecondsSinceEpoch.toString();

  static void notifyLoggedOut() => html.window.localStorage.remove(_key);

  // Diğer sekmelerden gelen auth değişimlerini dinle.
  // true → giriş yapıldı, false → çıkış yapıldı.
  static Stream<bool> get authChanges {
    return html.window.onStorage
        .where((e) => e.key == _key)
        .map((e) => e.newValue != null);
  }
}
