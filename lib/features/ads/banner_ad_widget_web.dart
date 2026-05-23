// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:async';
import 'dart:html' as html;
import 'dart:ui_web' as ui_web;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/config/remote_config_service.dart';
import '../../core/providers.dart';

class BannerAdWidget extends ConsumerStatefulWidget {
  const BannerAdWidget({super.key});

  @override
  ConsumerState<BannerAdWidget> createState() => _BannerAdWidgetState();
}

class _BannerAdWidgetState extends ConsumerState<BannerAdWidget> {
  static var _scriptLoaded = false;
  late final String _viewType;
  late final bool _adsEnabled;

  bool get _hasConfig =>
      AppConfig.adsenseClientId.isNotEmpty &&
      AppConfig.adsenseBannerSlotId.isNotEmpty;

  @override
  void initState() {
    super.initState();
    _adsEnabled = ref.read(remoteConfigProvider).getBool(RemoteConfigKeys.adsEnabled);
    _viewType = 'adsense-banner-${identityHashCode(this)}';
    if (!_hasConfig || !_adsEnabled) return;

    _ensureAdsenseScript();
    ui_web.platformViewRegistry.registerViewFactory(
      _viewType,
      (_) => _buildAdElement(),
    );
  }

  void _ensureAdsenseScript() {
    if (_scriptLoaded) return;
    _scriptLoaded = true;

    final script = html.ScriptElement()
      ..async = true
      ..crossOrigin = 'anonymous'
      ..src =
          'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js'
          '?client=${AppConfig.adsenseClientId}';
    html.document.head?.append(script);
  }

  html.Element _buildAdElement() {
    final wrapper = html.DivElement()
      ..style.width = '100%'
      ..style.minHeight = '90px'
      ..style.display = 'flex'
      ..style.alignItems = 'center'
      ..style.justifyContent = 'center';

    final ad = html.Element.tag('ins')
      ..classes.add('adsbygoogle')
      ..style.display = 'block'
      ..style.width = '100%'
      ..style.minHeight = '90px'
      ..setAttribute('data-ad-client', AppConfig.adsenseClientId)
      ..setAttribute('data-ad-slot', AppConfig.adsenseBannerSlotId)
      ..setAttribute('data-ad-format', 'auto')
      ..setAttribute('data-full-width-responsive', 'true');

    wrapper.append(ad);

    unawaited(Future<void>.delayed(Duration.zero, () {
      final pushScript = html.ScriptElement()
        ..text = '(adsbygoogle = window.adsbygoogle || []).push({});';
      wrapper.append(pushScript);
    }));

    return wrapper;
  }

  @override
  Widget build(BuildContext context) {
    if (!_hasConfig || !_adsEnabled) return const SizedBox.shrink();

    return SizedBox(
      height: 90,
      width: double.infinity,
      child: HtmlElementView(viewType: _viewType),
    );
  }
}
