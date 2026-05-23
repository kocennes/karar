import 'package:flutter/material.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';

class PostPollWidget extends StatelessWidget {
  const PostPollWidget({
    super.key,
    required this.poll,
    required this.onVote,
  });

  final PostPoll poll;
  final ValueChanged<String> onVote;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.symmetric(vertical: 8),
          child: Row(
            children: [
              Icon(Icons.poll_outlined, size: 18, color: AppColors.textSecondary),
              SizedBox(width: 8),
              Text(
                'Topluluk Anketi',
                style: TextStyle(
                  fontWeight: FontWeight.bold,
                  color: AppColors.textSecondary,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 8),
        ...poll.options.map((option) => _PollOptionTile(
              option: option,
              totalVotes: poll.totalVotes,
              isSelected: poll.mySelectionId == option.id,
              hasVoted: poll.hasVoted,
              onTap: () => onVote(option.id),
            )),
        Padding(
          padding: const EdgeInsets.only(top: 8, left: 4),
          child: Text(
            '${poll.totalVotes} oy kullanıldı',
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
                  color: AppColors.textTertiary,
                ),
          ),
        ),
      ],
    );
  }
}

class _PollOptionTile extends StatelessWidget {
  const _PollOptionTile({
    required this.option,
    required this.totalVotes,
    required this.isSelected,
    required this.hasVoted,
    required this.onTap,
  });

  final PollOption option;
  final int totalVotes;
  final bool isSelected;
  final bool hasVoted;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final percent = totalVotes == 0 ? 0.0 : option.voteCount / totalVotes;

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: InkWell(
        onTap: hasVoted ? null : onTap,
        borderRadius: BorderRadius.circular(12),
        child: Container(
          clipBehavior: Clip.antiAlias,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: isSelected
                  ? colorScheme.primary
                  : colorScheme.outlineVariant.withValues(alpha: 0.5),
              width: isSelected ? 2 : 1,
            ),
          ),
          child: Stack(
            children: [
              if (hasVoted)
                FractionallySizedBox(
                  widthFactor: percent,
                  child: Container(
                    height: 48,
                    color: isSelected
                        ? colorScheme.primary.withValues(alpha: 0.15)
                        : colorScheme.surfaceContainerHighest.withValues(alpha: 0.4),
                  ),
                ),
              Container(
                height: 48,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        option.text,
                        style: TextStyle(
                          fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
                          color: isSelected ? colorScheme.primary : null,
                        ),
                      ),
                    ),
                    if (hasVoted) ...[
                      const SizedBox(width: 12),
                      Text(
                        '%${(percent * 100).round()}',
                        style: TextStyle(
                          fontWeight: FontWeight.bold,
                          color: isSelected ? colorScheme.primary : AppColors.textSecondary,
                        ),
                      ),
                    ] else
                      Icon(
                        Icons.radio_button_off,
                        size: 20,
                        color: colorScheme.outlineVariant,
                      ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
