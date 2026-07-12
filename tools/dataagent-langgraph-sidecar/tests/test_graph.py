import unittest

from test_contracts import load_module, valid_request


class FakeApp:
    def __init__(self, node):
        self.node = node

    def invoke(self, state):
        return {**state, **self.node(state)}


class FakeStateGraph:
    instances = []

    def __init__(self, state_type):
        self.state_type = state_type
        self.node = None
        self.compile_count = 0
        FakeStateGraph.instances.append(self)

    def add_node(self, _name, node):
        self.node = node

    def set_entry_point(self, _name):
        pass

    def add_edge(self, _source, _target):
        pass

    def compile(self):
        self.compile_count += 1
        return FakeApp(self.node)


class GraphTests(unittest.TestCase):
    def test_graph_is_typed_compiled_once_and_reused(self):
        graph = load_module("graph")
        contracts = load_module("contracts")
        FakeStateGraph.instances.clear()
        app = graph.compile_advisory_graph(
            state_graph_factory=FakeStateGraph,
            end_marker="end",
        )
        request = contracts.validate_handshake_request(valid_request())

        first = app.invoke({"request": request})["response"]
        second = app.invoke({"request": request})["response"]

        self.assertEqual(1, len(FakeStateGraph.instances))
        workflow = FakeStateGraph.instances[0]
        self.assertIs(graph.AdvisoryGraphState, workflow.state_type)
        self.assertEqual(1, workflow.compile_count)
        self.assertFalse(first["FallbackRequired"])
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main()
