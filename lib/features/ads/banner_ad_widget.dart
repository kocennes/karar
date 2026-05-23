export 'banner_ad_widget_stub.dart'
    if (dart.library.html) 'banner_ad_widget_web.dart'
    if (dart.library.io) 'banner_ad_widget_mobile.dart';
