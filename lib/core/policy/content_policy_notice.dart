class ContentPolicyNotice {
  const ContentPolicyNotice({
    required this.version,
    required this.title,
    required this.summary,
    required this.announcedAt,
    required this.effectiveAt,
  });

  final String version;
  final String title;
  final String summary;
  final DateTime announcedAt;
  final DateTime effectiveAt;

  bool shouldShow(DateTime now) {
    final starts = DateTime(
      announcedAt.year,
      announcedAt.month,
      announcedAt.day,
    );
    final ends = DateTime(
      effectiveAt.year,
      effectiveAt.month,
      effectiveAt.day,
      23,
      59,
      59,
    );
    return !now.isBefore(starts) && !now.isAfter(ends);
  }
}

final activeContentPolicyNotice = ContentPolicyNotice(
  version: '2026-05-29-content-policy',
  title: 'İçerik politikası güncelleniyor',
  summary:
      'Yeni kurallar 29 Mayıs 2026 tarihinde yürürlüğe girer. Geçmiş içerikler geriye dönük değerlendirilmez.',
  announcedAt: DateTime(2026, 5, 22),
  effectiveAt: DateTime(2026, 5, 29),
);
