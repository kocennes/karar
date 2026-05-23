import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/login_nudge.dart';
import 'post_detail_provider.dart';
import 'user_mention_overlay.dart';

class CommentInput extends ConsumerStatefulWidget {
  const CommentInput({
    super.key,
    required this.postId,
    required this.onSubmit,
    this.isLoading = false,
  });

  final String postId;
  final ValueChanged<String> onSubmit;
  final bool isLoading;

  @override
  ConsumerState<CommentInput> createState() => _CommentInputState();
}

class _CommentInputState extends ConsumerState<CommentInput> {
  final _ctrl = TextEditingController();
  final _layerLink = LayerLink();
  OverlayEntry? _mentionOverlay;
  var _charCount = 0;
  String? _mentionQuery;

  @override
  void initState() {
    super.initState();
    _ctrl.addListener(_handleTextChange);
  }

  @override
  void dispose() {
    _hideMentionOverlay();
    _ctrl.removeListener(_handleTextChange);
    _ctrl.dispose();
    super.dispose();
  }

  void _handleTextChange() {
    setState(() => _charCount = _ctrl.text.length);
    _checkMentions();
  }

  void _checkMentions() {
    final text = _ctrl.text;
    final selection = _ctrl.selection;

    if (!selection.isValid || !selection.isCollapsed) {
      _hideMentionOverlay();
      return;
    }

    final cursorPosition = selection.baseOffset;
    final textBeforeCursor = text.substring(0, cursorPosition);
    final lastAtSign = textBeforeCursor.lastIndexOf('@');

    if (lastAtSign != -1 &&
        (lastAtSign == 0 || textBeforeCursor[lastAtSign - 1] == ' ')) {
      final query = textBeforeCursor.substring(lastAtSign + 1);
      if (!query.contains(' ')) {
        _showMentionOverlay(query);
        return;
      }
    }
    _hideMentionOverlay();
  }

  void _showMentionOverlay(String query) {
    _mentionQuery = query;
    if (_mentionOverlay == null) {
      _mentionOverlay = OverlayEntry(
        builder: (context) => Positioned(
          width: 260,
          child: CompositedTransformFollower(
            link: _layerLink,
            showWhenUnlinked: false,
            offset: const Offset(0, -210), // Show above input
            child: UserMentionOverlay(
              query: _mentionQuery!,
              onSelect: _insertMention,
            ),
          ),
        ),
      );
      Overlay.of(context).insert(_mentionOverlay!);
    } else {
      _mentionOverlay!.markNeedsBuild();
    }
  }

  void _hideMentionOverlay() {
    _mentionOverlay?.remove();
    _mentionOverlay = null;
    _mentionQuery = null;
  }

  void _insertMention(String username) {
    final text = _ctrl.text;
    final selection = _ctrl.selection;
    final cursorPosition = selection.baseOffset;
    final textBeforeCursor = text.substring(0, cursorPosition);
    final lastAtSign = textBeforeCursor.lastIndexOf('@');

    final newText =
        text.replaceRange(lastAtSign, cursorPosition, '@$username ');
    _ctrl.text = newText;
    _ctrl.selection =
        TextSelection.collapsed(offset: lastAtSign + username.length + 2);
    _hideMentionOverlay();
  }

  bool _checkLogin() {
    final isLoggedIn = ref.read(currentUserProvider) != null;
    if (!isLoggedIn) {
      LoginNudge.show(
        context,
        title: 'Yorum Yap',
        message:
            'Görüşlerini paylaşmak ve topluluğa katılmak için giriş yapmalısın.',
        returnTo: '/posts/${widget.postId}',
      );
      return false;
    }
    return true;
  }

  void _submit() {
    if (!_checkLogin()) return;
    final text = _ctrl.text.trim();
    if (Validators.comment(text) != null) return;

    widget.onSubmit(text);
    _ctrl.clear();
  }

  @override
  Widget build(BuildContext context) {
    final replyingTo = ref.watch(
      postDetailProvider(widget.postId).select((s) => s.replyingToComment),
    );
    final canSubmit = _charCount >= 5 && !widget.isLoading;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (replyingTo != null)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            color: Theme.of(context)
                .colorScheme
                .surfaceContainerHighest
                .withValues(alpha: 0.5),
            child: Row(
              children: [
                const Icon(Icons.reply, size: 16),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    '${replyingTo.authorName} kullanıcısına yanıt veriyorsun',
                    style: Theme.of(context).textTheme.labelSmall,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                IconButton(
                  tooltip: 'Yanıtlamayı iptal et',
                  icon: const Icon(Icons.close, size: 16),
                  onPressed: () => ref
                      .read(postDetailProvider(widget.postId).notifier)
                      .cancelReply(),
                  constraints: const BoxConstraints(
                    minWidth: 48,
                    minHeight: 48,
                  ),
                ),
              ],
            ),
          ),
        Container(
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            8,
            MediaQuery.of(context).viewInsets.bottom + 8,
          ),
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.surface,
            border: Border(
              top: BorderSide(
                color: Theme.of(context).colorScheme.outlineVariant,
              ),
            ),
          ),
          child: CompositedTransformTarget(
            link: _layerLink,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: TextField(
                    controller: _ctrl,
                    maxLength: 500,
                    maxLines: 4,
                    minLines: 1,
                    autofocus: replyingTo != null,
                    textInputAction: TextInputAction.newline,
                    onTap: () {
                      if (ref.read(currentUserProvider) == null) {
                        FocusScope.of(context).unfocus();
                        _checkLogin();
                      }
                    },
                    decoration: InputDecoration(
                      hintText: replyingTo != null
                          ? 'Yanıtını yaz…'
                          : 'Yorumunu yaz…',
                      border: InputBorder.none,
                      counterText: '$_charCount/500',
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                widget.isLoading
                    ? const Padding(
                        padding: EdgeInsets.all(12),
                        child: SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        ),
                      )
                    : IconButton.filled(
                        tooltip: replyingTo != null
                            ? 'Yanıt gönder'
                            : 'Yorum gönder',
                        onPressed: canSubmit ? _submit : null,
                        icon: const Icon(Icons.send),
                      ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}
