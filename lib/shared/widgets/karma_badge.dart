import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';

class _BadgeLevel {
  const _BadgeLevel({
    required this.emoji,
    required this.label,
    required this.minKarma,
    required this.maxKarma,
    required this.description,
  });

  final String emoji;
  final String label;
  final int minKarma;
  final int? maxKarma;
  final String description;
}

const _levels = [
  _BadgeLevel(
    emoji: '🌱',
    label: 'Yeni',
    minKarma: 0,
    maxKarma: 10,
    description: 'Platforma yeni katıldın. İlk paylaşımlarınla topluluğu tanı.',
  ),
  _BadgeLevel(
    emoji: '⚡',
    label: 'Aktif',
    minKarma: 11,
    maxKarma: 50,
    description: 'Düzenli paylaşım yapıyorsun ve topluluktan olumlu geri dönüş alıyorsun.',
  ),
  _BadgeLevel(
    emoji: '🔥',
    label: 'Popüler',
    minKarma: 51,
    maxKarma: 200,
    description: 'Paylaşımların çok oy ve yorum alıyor. Toplulukta tanınan birisin.',
  ),
  _BadgeLevel(
    emoji: '⚖️',
    label: 'Hakem',
    minKarma: 201,
    maxKarma: 500,
    description: 'Topluluk kararlarına önemli katkı sağlıyorsun. Yorumların rehber niteliğinde.',
  ),
  _BadgeLevel(
    emoji: '👑',
    label: 'Usta',
    minKarma: 501,
    maxKarma: null,
    description: 'Platformun en etkili seslerinden birisin. Topluluk sana güveniyor.',
  ),
];

int _levelIndex(int karma) {
  for (int i = _levels.length - 1; i >= 0; i--) {
    if (karma >= _levels[i].minKarma) return i;
  }
  return 0;
}

String karmaBadgeEmoji(int karma) => _levels[_levelIndex(karma)].emoji;
String karmaBadgeLabel(int karma) => _levels[_levelIndex(karma)].label;

class KarmaBadge extends StatelessWidget {
  const KarmaBadge({
    super.key,
    required this.karma,
    this.size = 16,
    this.showDetail = false,
  });

  final int karma;
  final double size;
  final bool showDetail;

  void _showSheet(BuildContext context) {
    showModalBottomSheet<void>(
      context: context,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => KarmaBadgeSheet(karma: karma),
    );
  }

  @override
  Widget build(BuildContext context) {
    final badge = Text(
      karmaBadgeEmoji(karma),
      style: TextStyle(fontSize: size),
    );

    if (!showDetail) return badge;

    return Semantics(
      button: true,
      label: '${karmaBadgeLabel(karma)} rozeti — detay için dokun',
      child: GestureDetector(
        onTap: () => _showSheet(context),
        child: badge,
      ),
    );
  }
}


class KarmaBadgeSheet extends StatelessWidget {
  const KarmaBadgeSheet({super.key, required this.karma});

  final int karma;

  @override
  Widget build(BuildContext context) {
    final currentIndex = _levelIndex(karma);
    final current = _levels[currentIndex];
    final hasNext = currentIndex < _levels.length - 1;
    final next = hasNext ? _levels[currentIndex + 1] : null;
    final remaining = next != null ? (next.minKarma - karma) : 0;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 20, 20, 8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Text(current.emoji, style: const TextStyle(fontSize: 32)),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        current.label,
                        style: Theme.of(context).textTheme.titleLarge?.copyWith(
                              fontWeight: FontWeight.w900,
                            ),
                      ),
                      Text(
                        '$karma karma',
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(
                              color: AppColors.textSecondary,
                            ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              current.description,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: AppColors.textSecondary,
                    height: 1.5,
                  ),
            ),
            const SizedBox(height: 20),
            Text(
              'Rozet Seviyeleri',
              style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    color: AppColors.textTertiary,
                    fontWeight: FontWeight.w700,
                  ),
            ),
            const SizedBox(height: 12),
            ...List.generate(_levels.length, (i) {
              final level = _levels[i];
              final isEarned = karma >= level.minKarma;
              final isCurrent = i == currentIndex;
              final rangeText = level.maxKarma != null
                  ? '${level.minKarma}–${level.maxKarma}'
                  : '${level.minKarma}+';

              return Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: Row(
                  children: [
                    Text(
                      level.emoji,
                      style: TextStyle(
                        fontSize: 20,
                        color: isEarned ? null : Colors.grey.withValues(alpha: 0.4),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            level.label,
                            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                                  fontWeight: isCurrent
                                      ? FontWeight.w800
                                      : FontWeight.normal,
                                  color: isEarned
                                      ? null
                                      : AppColors.textTertiary,
                                ),
                          ),
                          Text(
                            '$rangeText karma',
                            style: Theme.of(context).textTheme.labelSmall?.copyWith(
                                  color: AppColors.textTertiary,
                                ),
                          ),
                        ],
                      ),
                    ),
                    if (isCurrent)
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 8, vertical: 3),
                        decoration: BoxDecoration(
                          color: AppColors.accent.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: Text(
                          'Şu anki',
                          style: Theme.of(context).textTheme.labelSmall?.copyWith(
                                color: AppColors.accent,
                                fontWeight: FontWeight.w700,
                              ),
                        ),
                      )
                    else if (isEarned)
                      const Icon(Icons.check_circle,
                          size: 18, color: AppColors.hakli),
                  ],
                ),
              );
            }),
            if (hasNext && next != null) ...[
              const Divider(height: 24),
              Row(
                children: [
                  const Icon(Icons.trending_up,
                      size: 16, color: AppColors.textSecondary),
                  const SizedBox(width: 8),
                  Text(
                    'Sonraki rozet: ${next.emoji} ${next.label} — $remaining karma daha',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: AppColors.textSecondary,
                        ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
            ],
          ],
        ),
      ),
    );
  }
}
