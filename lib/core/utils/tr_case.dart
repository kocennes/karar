/// Turkish-aware case conversion.
///
/// Dart's [String.toUpperCase] / [String.toLowerCase] follow the root locale,
/// so `'i'.toUpperCase()` gives `I` (should be `İ`) and `'I'.toLowerCase()`
/// gives `i` (should be `ı`). Use these helpers whenever a single letter or a
/// short token has to change case (avatar initials, normalised lookups).
///
/// Copy is written in sentence case; never upper-case whole UI strings.
library;

/// Upper-cases [s] with Turkish dotted/dotless i rules (i→İ, ı→I).
String trUpper(String s) {
  if (s.isEmpty) return s;
  return s.replaceAll('i', 'İ').replaceAll('ı', 'I').toUpperCase();
}

/// Lower-cases [s] with Turkish dotted/dotless i rules (İ→i, I→ı).
String trLower(String s) {
  if (s.isEmpty) return s;
  return s.replaceAll('İ', 'i').replaceAll('I', 'ı').toLowerCase();
}

/// Lower-cases [s] the Turkish way and folds the Turkish diacritics to ASCII
/// (ç→c, ğ→g, ı→i, ö→o, ş→s, ü→u). Useful for tolerant lookups such as
/// matching category names ("İş Hayatı" → "is hayati").
String trFold(String s) {
  const map = {
    'ç': 'c',
    'ğ': 'g',
    'ı': 'i',
    'ö': 'o',
    'ş': 's',
    'ü': 'u',
    'â': 'a',
    'î': 'i',
    'û': 'u',
  };
  final lower = trLower(s.trim());
  final buffer = StringBuffer();
  for (final rune in lower.runes) {
    final ch = String.fromCharCode(rune);
    buffer.write(map[ch] ?? ch);
  }
  return buffer.toString();
}
