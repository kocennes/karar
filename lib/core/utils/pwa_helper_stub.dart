abstract final class PwaHelper {
  static bool get isInstalled => true;
  static bool get isPromptAvailable => false;
  static bool get isIosSafari => false;
  static void promptInstall() {}
  static void setOnInstallPromptListener(void Function() onPrompt) {}
}
