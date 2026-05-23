abstract final class DateFormatter {
  static String relative(DateTime dateTime) {
    final diff = DateTime.now().difference(dateTime.toLocal());
    if (diff.inSeconds < 60) return 'şimdi';
    if (diff.inMinutes < 60) return '${diff.inMinutes}dk önce';
    if (diff.inHours < 24) return '${diff.inHours}s önce';
    if (diff.inDays < 7) return '${diff.inDays}g önce';
    if (diff.inDays < 30) return '${(diff.inDays / 7).floor()}h önce';
    if (diff.inDays < 365) return '${(diff.inDays / 30).floor()}ay önce';
    return '${(diff.inDays / 365).floor()}y önce';
  }

  static String full(DateTime dateTime) {
    final d = dateTime.toLocal();
    return '${d.day.toString().padLeft(2, '0')}.${d.month.toString().padLeft(2, '0')}.${d.year} '
        '${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
  }
}
