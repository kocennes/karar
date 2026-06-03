import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('ad integration only allows banner ad formats', () {
    final dartFiles = [
      ...Directory('lib/core/ads').listSync(recursive: true).whereType<File>(),
      ...Directory('lib/features/ads')
          .listSync(recursive: true)
          .whereType<File>(),
      File('lib/core/config/app_config.dart'),
    ].where((file) => file.path.endsWith('.dart'));

    final source = dartFiles.map((file) => file.readAsStringSync()).join('\n');

    expect(source, contains('BannerAd'));
    expect(source, isNot(contains('InterstitialAd')));
    expect(source, isNot(contains('RewardedAd')));
    expect(source, isNot(contains('RewardedInterstitialAd')));
    expect(source.toLowerCase(), isNot(contains('interstitial')));
    expect(source.toLowerCase(), isNot(contains('rewarded')));
  });

  test('mobile banner ad collapses when ads are disabled or load fails', () {
    final source = File('lib/features/ads/banner_ad_widget_mobile.dart')
        .readAsStringSync();

    expect(source, contains('RemoteConfigKeys.adsEnabled'));
    expect(source, contains('var _adsEnabled = false;'));
    expect(source, contains('var _loadFailed = false;'));
    expect(source, contains('if (!_adsEnabled || _loadFailed)'));
    expect(source, contains('_loadFailed = true;'));
    expect(source, contains('if (!mounted) return;'));
  });

  test('web banner ad remains disabled when config or remote flag is missing',
      () {
    final source =
        File('lib/features/ads/banner_ad_widget_web.dart').readAsStringSync();

    expect(source, contains('AppConfig.adsenseClientId.isNotEmpty'));
    expect(source, contains('AppConfig.adsenseBannerSlotId.isNotEmpty'));
    expect(source, contains('RemoteConfigKeys.adsEnabled'));
    expect(source, contains('if (!_hasConfig || !_adsEnabled)'));
    expect(source, contains('return const SizedBox.shrink();'));
  });
}
