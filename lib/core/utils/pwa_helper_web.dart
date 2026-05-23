// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:html' as html;

abstract final class PwaHelper {
  static bool get isInstalled {
    try {
      return (html.window as dynamic)['isPwaInstalled']?.call() as bool? ??
          false;
    } catch (_) {
      return false;
    }
  }

  static bool get isPromptAvailable {
    try {
      return (html.window as dynamic)['deferredPrompt'] != null;
    } catch (_) {
      return false;
    }
  }

  static bool get isIosSafari {
    try {
      final ua = html.window.navigator.userAgent;
      final isIos =
          ua.contains('iPhone') || ua.contains('iPad') || ua.contains('iPod');
      final isSafari = ua.contains('Safari') &&
          !ua.contains('CriOS') &&
          !ua.contains('Chrome');
      return isIos && isSafari;
    } catch (_) {
      return false;
    }
  }

  static void promptInstall() {
    try {
      (html.window as dynamic)['showInstallPrompt']?.call();
    } catch (_) {}
  }

  static void setOnInstallPromptListener(void Function() onPrompt) {
    try {
      (html.window as dynamic)['onBeforeInstallPrompt'] = onPrompt;
      if (isPromptAvailable) onPrompt();
    } catch (_) {}
  }
}
