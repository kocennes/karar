import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _kFontSizeKey = 'app_font_size_factor';

enum AppFontSize {
  small(0.85, 'Küçük'),
  normal(1.0, 'Normal'),
  large(1.15, 'Büyük');

  const AppFontSize(this.factor, this.label);
  final double factor;
  final String label;
}

class FontSizeNotifier extends Notifier<AppFontSize> {
  @override
  AppFontSize build() {
    _loadFromPrefs();
    return AppFontSize.normal;
  }

  Future<void> _loadFromPrefs() async {
    final prefs = await SharedPreferences.getInstance();
    final value = prefs.getString(_kFontSizeKey);
    if (value != null) {
      state = AppFontSize.values.firstWhere(
        (e) => e.name == value,
        orElse: () => AppFontSize.normal,
      );
    }
  }

  Future<void> setFontSize(AppFontSize fontSize) async {
    state = fontSize;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kFontSizeKey, fontSize.name);
  }
}

final fontSizeProvider =
    NotifierProvider<FontSizeNotifier, AppFontSize>(FontSizeNotifier.new);
