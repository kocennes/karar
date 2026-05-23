abstract final class AppConfig {
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://localhost:5088',
  );

  static const requireSecureApiTransport = bool.fromEnvironment(
    'REQUIRE_SECURE_API_TRANSPORT',
    defaultValue: false,
  );

  static void validate() {
    final uri = Uri.tryParse(apiBaseUrl);
    if (requireSecureApiTransport &&
        (uri == null || uri.scheme.toLowerCase() != 'https')) {
      throw StateError(
        'Production API_BASE_URL must use HTTPS when '
        'REQUIRE_SECURE_API_TRANSPORT=true.',
      );
    }
  }

  static const webVapidKey = String.fromEnvironment(
    'WEB_VAPID_KEY',
    defaultValue: '',
  );

  static const adsenseClientId = String.fromEnvironment(
    'ADSENSE_CLIENT_ID',
    defaultValue: '',
  );

  static const adsenseBannerSlotId = String.fromEnvironment(
    'ADSENSE_BANNER_SLOT_ID',
    defaultValue: '',
  );

  // AdMob — test ID'leri (production'da gerçek ID ile değiştir)
  static const androidBannerAdUnitId = 'ca-app-pub-3940256099942544/6300978111';
  static const iosBannerAdUnitId = 'ca-app-pub-3940256099942544/2934735716';

  // Legal URLs
  static const privacyPolicyUrl = 'https://karar.app/legal/privacy';
  static const termsOfServiceUrl = 'https://karar.app/legal/terms';
  static const communityGuidelinesUrl = 'https://karar.app/legal/community';
  static const supportEmail = 'destek@karar.app';
}
