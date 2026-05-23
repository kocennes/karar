import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/features/post_detail/vote_bar.dart';
import 'package:karar/shared/models/post.dart';

void main() {
  testWidgets('VoteBar shows correct percentages', (tester) async {
    const category = Category(id: 1, name: 'Test', icon: 'T');
    final post = Post(
      id: '1',
      category: category,
      title: 'Title',
      content: 'Content',
      createdAgo: '1h ago',
      voteCountHakli: 75,
      voteCountHaksiz: 25,
      commentCount: 10,
      comments: const [],
      createdAt: DateTime.now(),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: VoteBar(post: post),
        ),
      ),
    );

    expect(find.text('Haklı  %75'), findsOneWidget);
    expect(find.text('Haksız  %25'), findsOneWidget);
    expect(find.byType(Flexible), findsAtLeastNWidgets(2));
  });
}
