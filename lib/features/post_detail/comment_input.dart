import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/validators.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/login_nudge.dart';
import 'post_detail_provider.dart';
import 'user_mention_overlay.dart';

/// Comment composer (spec §6.2): surface bar with a top hairline, an
/// optional reply-context chip, a muted pill input and a circular send
/// button (brand fill when enabled). The "{n}/500" counter only shows while
/// the field is focused or from 400 characters on (Haksız colour ≥ 480).
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
  static const _maxLength = 500;

  final _ctrl = TextEditingController();
  final _focusNode = FocusNode();
  final _layerLink = LayerLink();
  OverlayEntry? _mentionOverlay;
  var _charCount = 0;
  String? _mentionQuery;

  @override
  void initState() {
    super.initState();
    _ctrl.addListener(_handleTextChange);
    _focusNode.addListener(_handleFocusChange);
  }

  @override
  void dispose() {
    _hideMentionOverlay();
    _ctrl.removeListener(_handleTextChange);
    _ctrl.dispose();
    _focusNode.removeListener(_handleFocusChange);
    _focusNode.dispose();
    super.dispose();
  }

  void _handleFocusChange() {
    if (mounted) setState(() {});
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
    final kc = context.kc;
    final replyingTo = ref.watch(
      postDetailProvider(widget.postId).select((s) => s.replyingToComment),
    );
    final canSubmit = _charCount >= 5 && !widget.isLoading;
    final showCounter = _focusNode.hasFocus || _charCount >= 400;
    final keyboardInset = MediaQuery.viewInsetsOf(context).bottom;
    final bottomInset =
        keyboardInset > 0 ? keyboardInset : MediaQuery.paddingOf(context).bottom;
    final inputRadius = KararRadius.all(24);
    final textStyle = AppTypography.body.copyWith(color: kc.text1);
    // Dark: the muted pill sits only 1.08:1 off the elevated bar, so it gets
    // a 1 px outline rim (1.6:1) to keep its shape. Light: no border.
    final restingSide = kc.isDark
        ? BorderSide(color: kc.outline.withValues(alpha: 0.4))
        : BorderSide.none;

    return Material(
      color: kc.isDark ? kc.elevated : kc.surface,
      child: DecoratedBox(
        decoration: BoxDecoration(
          border: Border(top: BorderSide(color: kc.outlineVariant)),
        ),
        child: Padding(
          padding: EdgeInsets.fromLTRB(12, 10, 12, bottomInset + 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (replyingTo != null) ...[
                Container(
                  padding: const EdgeInsets.only(left: 12),
                  decoration: BoxDecoration(
                    color: kc.primaryContainer,
                    borderRadius: KararRadius.all(KararRadius.avatar),
                  ),
                  child: Row(
                    children: [
                      Icon(
                        Icons.reply_rounded,
                        size: 14,
                        color: kc.onPrimaryContainer,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          '${replyingTo.authorName ?? 'anonim'} kullanıcısına yanıt veriyorsun',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: AppTypography.meta.copyWith(
                            fontWeight: FontWeight.w700,
                            letterSpacing: 0,
                            color: kc.onPrimaryContainer,
                          ),
                        ),
                      ),
                      IconButton(
                        tooltip: 'Yanıtlamayı iptal et',
                        icon: const Icon(Icons.close_rounded, size: 18),
                        color: kc.onPrimaryContainer,
                        onPressed: () => ref
                            .read(postDetailProvider(widget.postId).notifier)
                            .cancelReply(),
                        constraints: const BoxConstraints(
                          minWidth: 44,
                          minHeight: 44,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 8),
              ],
              CompositedTransformTarget(
                link: _layerLink,
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _ctrl,
                        focusNode: _focusNode,
                        maxLength: _maxLength,
                        maxLines: 4,
                        minLines: 1,
                        autofocus: replyingTo != null,
                        textInputAction: TextInputAction.newline,
                        style: textStyle,
                        cursorColor: kc.primary,
                        buildCounter: (
                          context, {
                          required currentLength,
                          required isFocused,
                          maxLength,
                        }) =>
                            null,
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
                          hintStyle: textStyle.copyWith(color: kc.text3),
                          filled: true,
                          fillColor: kc.muted,
                          isDense: true,
                          constraints: const BoxConstraints(minHeight: 48),
                          contentPadding: const EdgeInsets.symmetric(
                            horizontal: 18,
                            vertical: 13,
                          ),
                          border: OutlineInputBorder(
                            borderRadius: inputRadius,
                            borderSide: restingSide,
                          ),
                          enabledBorder: OutlineInputBorder(
                            borderRadius: inputRadius,
                            borderSide: restingSide,
                          ),
                          focusedBorder: OutlineInputBorder(
                            borderRadius: inputRadius,
                            borderSide:
                                BorderSide(color: kc.primary, width: 1.5),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Padding(
                      padding: const EdgeInsets.only(bottom: 2),
                      child: widget.isLoading
                          ? SizedBox(
                              width: 44,
                              height: 44,
                              child: Center(
                                child: SizedBox(
                                  width: 20,
                                  height: 20,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                    color: kc.primary,
                                  ),
                                ),
                              ),
                            )
                          : KararCircleButton(
                              icon: Icons.arrow_upward_rounded,
                              tooltip: replyingTo != null
                                  ? 'Yanıt gönder'
                                  : 'Yorum gönder',
                              onPressed: canSubmit ? _submit : null,
                              style: KararCircleStyle.primary,
                              size: 44,
                            ),
                    ),
                  ],
                ),
              ),
              if (showCounter)
                Padding(
                  padding: const EdgeInsets.only(top: 4, right: 58),
                  child: Text(
                    '$_charCount/$_maxLength',
                    textAlign: TextAlign.end,
                    style: AppTypography.meta.copyWith(
                      color: _charCount >= 480 ? kc.haksizText : kc.text3,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
