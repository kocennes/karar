/// Compact, Turkish-style number formatting for counters (rail capsules,
/// tiles, hero, digest).
///
/// * `< 1 000`        → `456`
/// * `1 000 – 9 999`  → `1.247` (Turkish thousands dot)
/// * `≥ 10 000`       → `12,4B` (bin), decimals truncated, `,0` dropped
/// * `≥ 1 000 000`    → `1,2Mn`
///
/// Values are truncated (not rounded) so a counter never shows more than it
/// really has (9 999 stays `9.999`, 99 990 is `99,9B`).
String formatCompactCount(int n) {
  if (n < 0) return '-${formatCompactCount(-n)}';
  if (n < 1000) return '$n';
  if (n < 10000) {
    final thousands = n ~/ 1000;
    final rest = (n % 1000).toString().padLeft(3, '0');
    return '$thousands.$rest';
  }
  if (n < 1000000) return _withSuffix(n ~/ 100, 'B');
  return _withSuffix(n ~/ 100000, 'Mn');
}

/// [tenths] is the value in tenths of the unit (12,4 → 124).
String _withSuffix(int tenths, String suffix) {
  final whole = tenths ~/ 10;
  final fraction = tenths % 10;
  return fraction == 0 ? '$whole$suffix' : '$whole,$fraction$suffix';
}

/// Turkish percent notation: `%73`.
String formatPercent(int percent) => '%$percent';
