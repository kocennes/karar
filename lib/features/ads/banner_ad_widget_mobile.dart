import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_mobile_ads/google_mobile_ads.dart';

import '../../core/config/app_config.dart';
import '../../core/config/remote_config_service.dart';
import '../../core/providers.dart';

class BannerAdWidget extends ConsumerStatefulWidget {
  const BannerAdWidget({super.key});

  @override
  ConsumerState<BannerAdWidget> createState() => _BannerAdWidgetState();
}

class _BannerAdWidgetState extends ConsumerState<BannerAdWidget> {
  BannerAd? _ad;
  var _loaded = false;

  @override
  void initState() {
    super.initState();
    final adsEnabled = ref.read(remoteConfigProvider).getBool(RemoteConfigKeys.adsEnabled);
    if (adsEnabled) _loadAd();
  }

  void _loadAd() {
    final adUnitId = Platform.isAndroid
        ? AppConfig.androidBannerAdUnitId
        : AppConfig.iosBannerAdUnitId;

    _ad = BannerAd(
      adUnitId: adUnitId,
      size: AdSize.banner,
      request: const AdRequest(),
      listener: BannerAdListener(
        onAdLoaded: (_) => setState(() => _loaded = true),
        onAdFailedToLoad: (ad, _) {
          ad.dispose();
          setState(() => _ad = null);
        },
      ),
    )..load();
  }

  @override
  void dispose() {
    _ad?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_ad == null) return const SizedBox.shrink();
    
    return Container(
      height: 50,
      width: double.infinity,
      alignment: Alignment.center,
      color: Colors.transparent,
      child: _loaded ? AdWidget(ad: _ad!) : const SizedBox.shrink(),
    );
  }
}
