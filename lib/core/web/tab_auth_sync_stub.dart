abstract final class TabAuthSync {
  static void notifyLoggedIn() {}
  static void notifyLoggedOut() {}
  static Stream<bool> get authChanges => const Stream.empty();
}
